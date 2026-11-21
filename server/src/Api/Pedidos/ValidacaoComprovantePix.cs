using System.Buffers.Binary;
using SkiaSharp;
using System.Text;
using System.Text.RegularExpressions;

namespace Xaquiamuni.Api.Pedidos;

internal static partial class ValidacaoComprovantePix
{
    private const int MaxPixels = 16_000_000;

    internal static string? TipoValidado(string nome, string? mime, byte[] bytes)
    {
        var extensao = Path.GetExtension(nome).ToLowerInvariant();
        return extensao switch
        {
            ".pdf" when MimeIgual(mime, "application/pdf") && Pdf(bytes) => "application/pdf",
            ".png" when MimeIgual(mime, "image/png") && Png(bytes) && DecodificarImagem(bytes, SKEncodedImageFormat.Png) => "image/png",
            ".jpg" or ".jpeg" when MimeIgual(mime, "image/jpeg") && Jpeg(bytes) && DecodificarImagem(bytes, SKEncodedImageFormat.Jpeg) => "image/jpeg",
            _ => null
        };
    }

    private static bool MimeIgual(string? declarado, string esperado) =>
        string.Equals(declarado, esperado, StringComparison.OrdinalIgnoreCase);

    private static bool Png(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || bytes[0] != 137 || !bytes[1..].StartsWith("PNG\r\n\u001a\n"u8)) return false;
        var tamanhoComprimido = 0;
        int altura = 0, largura = 0, bits = 0, cor = -1;
        bool paleta = false, houveDados = false, encerrouDados = false;
        var pos = 8;
        while (pos <= bytes.Length - 12)
        {
            uint tamanho = BinaryPrimitives.ReadUInt32BigEndian(bytes[pos..]);
            if (tamanho > bytes.Length - pos - 12) return false;
            var nome = bytes.Slice(pos + 4, 4);
            var dados = bytes.Slice(pos + 8, (int)tamanho);
            if (Crc(bytes.Slice(pos + 4, 4 + dados.Length)) !=
                BinaryPrimitives.ReadUInt32BigEndian(bytes[(pos + 8 + dados.Length)..])) return false;
            pos += dados.Length + 12;
            if (pos == 33 && nome.SequenceEqual("IHDR"u8) && dados.Length == 13)
            {
                largura = BinaryPrimitives.ReadInt32BigEndian(dados);
                altura = BinaryPrimitives.ReadInt32BigEndian(dados[4..]);
                bits = dados[8];
                cor = dados[9];
                if (!DimensoesValidas(largura, altura) || dados[10] != 0 || dados[11] != 0 || dados[12] > 1 ||
                    !BitsPngValidos(cor, bits)) return false;
            }
            else if (cor < 0) return false;
            else if (nome.SequenceEqual("PLTE"u8))
            {
                if (paleta || houveDados || cor is 0 or 4 || dados.Length is 0 or > 768 ||
                    dados.Length % 3 != 0 || (cor == 3 && dados.Length / 3 > (1 << bits))) return false;
                paleta = true;
            }
            else if (nome.SequenceEqual("IDAT"u8))
            {
                if (encerrouDados || (cor == 3 && !paleta)) return false;
                houveDados = true;
                tamanhoComprimido += dados.Length;
            }
            else if (nome.SequenceEqual("IEND"u8))
            {
                return dados.Length == 0 && pos == bytes.Length && tamanhoComprimido > 0;
            }
            else
            {
                // Ignora metadados auxiliares sem descompactá-los.
                if (nome[0] is < (byte)'a' or > (byte)'z') return false;
                encerrouDados |= houveDados;
            }
        }

        return false;
    }

    private static bool BitsPngValidos(int cor, int bits) => cor switch
    {
        0 => bits is 1 or 2 or 4 or 8 or 16,
        3 => bits is 1 or 2 or 4 or 8,
        2 or 4 or 6 => bits is 8 or 16,
        _ => false
    };

    private static bool DecodificarImagem(byte[] bytes, SKEncodedImageFormat formato)
    {
        using var dados = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(dados);
        if (codec is null || codec.EncodedFormat != formato || codec.FrameCount > 1 || codec.RepetitionCount != 0 ||
            !DimensoesValidas(codec.Info.Width, codec.Info.Height)) return false;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        var resultado = codec.GetPixels(info, bitmap.GetPixels());
        return resultado == SKCodecResult.Success && bitmap.ReadyToDraw;
    }

    private static uint Crc(ReadOnlySpan<byte> dados)
    {
        uint crc = uint.MaxValue;
        foreach (var b in dados) crc = TabelaCrc[(crc ^ b) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static readonly uint[] TabelaCrc = Enumerable.Range(0, 256).Select(i =>
    {
        uint crc = (uint)i;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        return crc;
    }).ToArray();

    private static bool DimensoesValidas(int largura, int altura) =>
        largura is > 0 and <= 8192 && altura is > 0 and <= 8192 && (long)largura * altura <= MaxPixels;

    private static bool Jpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16 || bytes[0] != 255 || bytes[1] != 216) return false;
        var pos = 2;
        int componentes = 0, scans = 0;
        bool tabelaQuantizacao = false, tabelaHuffman = false;
        while (pos < bytes.Length)
        {
            if (bytes[pos++] != 255) return false;
            while (pos < bytes.Length && bytes[pos] == 255) pos++;
            if (pos == bytes.Length) return false;
            int marcador = bytes[pos++];
            if (marcador == 217) return scans > 0 && pos == bytes.Length;
            if (marcador is not (192 or 193 or 194 or 196 or 219 or 221 or 218 or 254 or >= 224 and <= 239))
                return false;
            if (pos > bytes.Length - 2) return false;
            int tamanho = BinaryPrimitives.ReadUInt16BigEndian(bytes[pos..]);
            if (tamanho < 2 || tamanho > bytes.Length - pos) return false;
            var dados = bytes.Slice(pos + 2, tamanho - 2);
            pos += tamanho;
            if (marcador is 192 or 193 or 194)
            {
                if (componentes != 0 || dados.Length < 6 || dados[0] != 8 ||
                    !DimensoesValidas(BinaryPrimitives.ReadUInt16BigEndian(dados[3..]),
                        BinaryPrimitives.ReadUInt16BigEndian(dados[1..]))) return false;
                componentes = dados[5];
                if (componentes is not (1 or 3 or 4) || dados.Length != 6 + componentes * 3) return false;
            }
            if (marcador == 219) tabelaQuantizacao |= dados.Length >= 65;
            if (marcador == 196) tabelaHuffman |= dados.Length >= 18;
            if (marcador != 218) continue;
            if (componentes == 0 || !tabelaQuantizacao || !tabelaHuffman || ++scans > 64 ||
                dados.Length < 6 || dados[0] == 0 || dados[0] > componentes || dados.Length != 4 + 2 * dados[0])
                return false;
            int inicio = pos;
            while (pos < bytes.Length - 1)
            {
                if (bytes[pos] != 255) { pos++; continue; }
                if (bytes[pos + 1] is 0 or >= 208 and <= 215) { pos += 2; continue; }
                break;
            }
            if (pos == inicio) return false;
        }

        return false;
    }

    private static bool Pdf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 80 || !bytes.StartsWith("%PDF-"u8)) return false;
        var texto = Encoding.Latin1.GetString(bytes).TrimEnd(' ', '\r', '\n', '\t');
        if (texto.Length < 9 || texto[5] is not ('1' or '2') || texto[6] != '.' ||
            !char.IsAsciiDigit(texto[7]) || texto[8] is not ('\r' or '\n') ||
            !texto.EndsWith("%%EOF", StringComparison.Ordinal) || PdfRecusado().IsMatch(texto)) return false;
        int inicio = texto.LastIndexOf("startxref", StringComparison.Ordinal);
        if (inicio < 0 || !int.TryParse(texto.AsSpan(inicio + 9, texto.Length - inicio - 14).Trim(), out int xref) ||
            xref < 9 || xref >= inicio || !texto.AsSpan(xref).StartsWith("xref", StringComparison.Ordinal)) return false;
        var tabela = texto.AsSpan(xref + 4, inicio - xref - 4);
        int trailer = tabela.IndexOf("trailer", StringComparison.Ordinal);
        if (trailer < 0) return false;
        var raiz = RaizPdf().Match(tabela[(trailer + 7)..].ToString());
        if (!raiz.Success || !int.TryParse(raiz.Groups[1].Value, out int raizId) ||
            !int.TryParse(raiz.Groups[2].Value, out int raizGeracao)) return false;
        bool catalogo = false;

        var linhas = tabela[..trailer].Trim().ToString().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int objetos = 0;
        for (int i = 0; i < linhas.Length;)
        {
            var cabecalho = linhas[i++].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cabecalho.Length != 2 || !int.TryParse(cabecalho[0], out int objeto) || objeto is < 0 or > 100_000 ||
                !int.TryParse(cabecalho[1], out int qtd) || qtd < 1 || qtd > linhas.Length - i || qtd > 10000 - objetos)
                return false;
            for (int j = 0; j < qtd; j++)
            {
                var linha = linhas[i++].Trim();
                if (linha.Length != 18 || linha[10] != ' ' || linha[16] != ' ' ||
                    !int.TryParse(linha.AsSpan(0, 10), out int pos) ||
                    !int.TryParse(linha.AsSpan(11, 5), out int geracao)) return false;
                if (linha[17] == 'f') continue;
                if (linha[17] != 'n' || pos < 9 || pos >= xref ||
                    !texto.AsSpan(pos).StartsWith($"{objeto + j} {geracao} obj", StringComparison.Ordinal))
                    return false;
                if (objeto + j == raizId && geracao == raizGeracao)
                {
                    var corpo = texto.AsSpan(pos, xref - pos);
                    int fimObjeto = corpo.IndexOf("endobj", StringComparison.Ordinal);
                    if (fimObjeto < 0 || fimObjeto > 4096 ||
                        !CatalogoPdf().IsMatch(corpo[..fimObjeto].ToString())) return false;
                    catalogo = true;
                }
                objetos++;
            }
        }
        return catalogo;
    }

    // Perfil conservador: PDF clássico, sem objetos comprimidos, ações ou anexos.
    [GeneratedRegex(@"/(?:[^\s<>\[\](){}/]*#|JavaScript\b|JS\b|Launch\b|OpenAction\b|Action\b|URI\b|AA\b|EmbeddedFile[s]?\b|RichMedia\b|XFA\b|Encrypt\b|ObjStm\b|Prev\b|XRefStm\b|SubmitForm\b|GoToR\b)", RegexOptions.NonBacktracking)]
    private static partial Regex PdfRecusado();

    [GeneratedRegex(@"/Root\s+(\d+)\s+(\d+)\s+R\b", RegexOptions.NonBacktracking)]
    private static partial Regex RaizPdf();

    [GeneratedRegex(@"/Type\s*/Catalog\b", RegexOptions.NonBacktracking)]
    private static partial Regex CatalogoPdf();
}

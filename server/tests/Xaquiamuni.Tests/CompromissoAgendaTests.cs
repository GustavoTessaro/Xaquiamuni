using Xaquiamuni.Dominio.Agenda;

namespace Xaquiamuni.Tests;

public sealed class CompromissoAgendaTests
{
    private static readonly Guid Autor = Guid.NewGuid();
    private static readonly DateOnly Dia = new(2026, 11, 30);
    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void DiaInteiroMantemDataCivilERegistraAuditoriaUtc()
    {
        var compromisso = CompromissoAgenda.Criar(" Feira ", " ", null, Dia, null, null, Autor, Agora);
        Assert.Equal("Feira", compromisso.Titulo);
        Assert.Null(compromisso.Descricao);
        Assert.Null(compromisso.Categoria);
        Assert.Null(compromisso.HorarioInicial);
        Assert.Equal(Dia, compromisso.Data);
        Assert.Equal(TimeSpan.Zero, compromisso.CriadoEmUtc.Offset);
        Assert.Equal(Agora, compromisso.AtualizadoEmUtc);
    }

    [Fact]
    public void AlteracaoComHorarioEExclusaoMantemAutorOriginal()
    {
        var compromisso = CompromissoAgenda.Criar("Feira", null, null, Dia, null, null, Autor, Agora);
        var versao = compromisso.Versao;
        var editor = Guid.NewGuid();
        compromisso.Alterar("Feira", "Levar material", "Eventos", Dia, new(9, 0), new(10, 30), editor, Agora.AddMinutes(1));
        Assert.Equal(new TimeOnly(10, 30), compromisso.HorarioFinal);
        Assert.Equal(Autor, compromisso.CriadoPorId);
        Assert.Equal(editor, compromisso.AtualizadoPorId);
        Assert.NotEqual(versao, compromisso.Versao);
        compromisso.Excluir(editor, Agora.AddMinutes(2));
        Assert.Equal(editor, compromisso.ExcluidoPorId);
        Assert.Equal(Agora.AddMinutes(2), compromisso.ExcluidoEmUtc);
        Assert.Throws<InvalidOperationException>(() => compromisso.Excluir(editor, Agora.AddMinutes(3)));
    }

    [Fact]
    public void ValidaTituloTextosEIntervalo()
    {
        Assert.Throws<ArgumentException>(() => CompromissoAgenda.Criar(" ", null, null, Dia, null, null, Autor, Agora));
        Assert.Throws<ArgumentException>(() => CompromissoAgenda.Criar(new string('x', 121), null, null, Dia, null, null, Autor, Agora));
        Assert.Throws<ArgumentException>(() => CompromissoAgenda.Criar("Feira", null, null, Dia, null, new(11, 0), Autor, Agora));
        Assert.Throws<ArgumentException>(() => CompromissoAgenda.Criar("Feira", null, null, Dia, new(11, 0), new(10, 0), Autor, Agora));
        Assert.Throws<ArgumentException>(() => CompromissoAgenda.Criar("Feira", null, null, Dia, new(11, 0), new(11, 0), Autor, Agora));
    }
}

import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, catchError, distinctUntilChanged, filter, map, merge, of, switchMap } from 'rxjs';
import {
  CatalogoPublicoRetorno,
  ProdutoCatalogoCaracteristica,
  ProdutoCatalogoDetalhe,
  ProdutoCatalogoImagem,
  ProdutoCatalogoOpcao,
  ProdutoCatalogoValor,
  CatalogoPublicoService,
} from './catalogo-publico.service';
import { formatarUnidadeVenda } from './catalogo-formatacao';
import { CarrinhoService } from './carrinho.service';

type ProdutoResult =
  | { id: string; product: ProdutoCatalogoDetalhe; failed: false }
  | { id: string; error: unknown; failed: true };

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-produto-detalhe',
  styleUrl: './produto-detalhe.component.css',
  templateUrl: './produto-detalhe.component.html',
})
export class ProdutoDetalheComponent implements OnInit {
  protected readonly product = signal<ProdutoCatalogoDetalhe | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly requestError = signal(false);
  protected readonly imageUnavailable = signal(false);
  protected readonly unavailableThumbnailIds = signal<ReadonlySet<string>>(new Set());
  protected readonly selectedOptionId = signal<string | null>(null);
  protected readonly selectedValues = signal<Record<string, string>>({});
  protected readonly selectedImageId = signal<string | null>(null);
  protected readonly selectionNotice = signal('');
  protected readonly mensagemCarrinho = signal('');
  protected readonly returnFilters = signal<CatalogoPublicoRetorno>({});
  protected readonly selectedOption = computed(() => {
    const optionId = this.selectedOptionId();
    return this.product()?.opcoes.find((option) => option.id === optionId) ?? null;
  });
  protected readonly gallery = computed(() => {
    const product = this.product();
    if (!product) {
      return [];
    }

    const optionId = this.selectedOptionId();
    const productImages = product.galeria.length ? product.galeria : [product.fotoPrincipal];
    const linkedImages = optionId
      ? productImages.filter((image) => image.opcaoIds.includes(optionId))
      : [];
    const generalImages = productImages.filter((image) => image.opcaoIds.length === 0);
    const relevantImages = optionId
      ? linkedImages.length || generalImages.length
        ? [...linkedImages, ...generalImages]
        : [product.fotoPrincipal]
      : [...productImages, product.fotoPrincipal];
    return [...new Map(relevantImages.map((image) => [image.id, image])).values()];
  });
  protected readonly selectedImage = computed(
    () => this.gallery().find((image) => image.id === this.selectedImageId()) ?? null,
  );

  private readonly route = inject(ActivatedRoute);
  private readonly catalogService = inject(CatalogoPublicoService);
  private readonly carrinho = inject(CarrinhoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly retryRequests = new Subject<string>();
  private currentProductId: string | null = null;

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.returnFilters.set({
        busca: params.get('busca')?.trim() || undefined,
        categoriaId: params.get('categoriaId') || undefined,
      });
    });

    const routeProductIds = this.route.paramMap.pipe(
      map((params) => params.get('id')),
      filter((id): id is string => id !== null && id.length > 0),
      distinctUntilChanged(),
    );

    merge(routeProductIds, this.retryRequests)
      .pipe(
        map((id) => {
          this.beginLoading(id);
          return id;
        }),
        switchMap((id) =>
          this.catalogService.consultarProduto(id).pipe(
            map((product) => ({ id, product, failed: false as const })),
            catchError((error: unknown) => of({ id, error, failed: true as const })),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.applyResult(result));
  }

  protected retry(): void {
    if (this.currentProductId) {
      this.retryRequests.next(this.currentProductId);
    }
  }

  protected formatPrice(price: number): string {
    return new Intl.NumberFormat('pt-BR', {
      style: 'currency',
      currency: 'BRL',
    }).format(price);
  }

  protected adicionarAoCarrinho(): void {
    const produto = this.product();
    const opcao = this.selectedOption();
    if (!produto || !opcao) {
      return;
    }
    const resultado = this.carrinho.adicionar(produto, opcao.id);
    this.mensagemCarrinho.set(
      resultado === 'ok'
        ? 'Item adicionado ao carrinho.'
        : resultado === 'indisponivel'
          ? 'Esta opção está indisponível para adicionar ao carrinho.'
          : 'Não foi possível adicionar esta opção ao carrinho.',
    );
  }

  protected formatMeasure(value: number, unit: 'g' | 'ml'): string {
    return `${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 }).format(value)} ${unit}`;
  }

  protected formatUnit(unit: string): string {
    return formatarUnidadeVenda(unit);
  }

  protected measures(option: ProdutoCatalogoOpcao | null): string[] {
    if (!option) {
      return [];
    }
    const result: string[] = [];
    if (option.pesoLiquidoGramas !== null) {
      result.push(this.formatMeasure(option.pesoLiquidoGramas, 'g'));
    }
    if (option.volumeLiquidoMililitros !== null) {
      result.push(this.formatMeasure(option.volumeLiquidoMililitros, 'ml'));
    }
    return result;
  }

  protected availableValues(characteristicId: string): ProdutoCatalogoValor[] {
    const product = this.product();
    if (!product) {
      return [];
    }

    const available = new Map<string, ProdutoCatalogoValor>();
    for (const option of product.opcoes) {
      const selectedValue = option.caracteristicas.find(
        (characteristic) => characteristic.caracteristicaId === characteristicId,
      );
      if (selectedValue) {
        available.set(selectedValue.valorId, {
          id: selectedValue.valorId,
          nome: selectedValue.valorNome,
          valorMedidaCanonico: selectedValue.valorMedidaCanonico,
        });
      }
    }
    return [...available.values()];
  }

  protected selectedValueName(characteristic: ProdutoCatalogoCaracteristica): string {
    const valueId = this.selectedValues()[characteristic.id];
    return characteristic.valores.find((value) => value.id === valueId)?.nome ?? '';
  }

  protected optionLabel(option: ProdutoCatalogoOpcao, index: number): string {
    const identification = option.identificacao?.trim();
    if (
      !identification ||
      identification
        .normalize('NFD')
        .replace(/\p{Diacritic}/gu, '')
        .toLowerCase() === 'padrao'
    ) {
      return `Opção ${index + 1}`;
    }
    return identification;
  }

  protected selectCharacteristic(characteristicId: string, event: Event): void {
    const target = event.target;
    if (!(target instanceof HTMLSelectElement) || !target.value) {
      return;
    }
    const product = this.product();
    if (!product) {
      return;
    }

    const previousValues = this.selectedValues();
    const candidates = product.opcoes.filter((option) =>
      option.caracteristicas.some(
        (characteristic) =>
          characteristic.caracteristicaId === characteristicId &&
          characteristic.valorId === target.value,
      ),
    );
    const matchingSelection = candidates.find((option) =>
      option.caracteristicas.every(
        (characteristic) =>
          characteristic.caracteristicaId === characteristicId ||
          previousValues[characteristic.caracteristicaId] === undefined ||
          previousValues[characteristic.caracteristicaId] === characteristic.valorId,
      ),
    );
    const nextOption = matchingSelection ?? candidates[0];
    if (!nextOption) {
      return;
    }

    const selectionWasAdjusted = Object.entries(previousValues).some(
      ([id, valueId]) =>
        id !== characteristicId &&
        nextOption.caracteristicas.find((item) => item.caracteristicaId === id)?.valorId !==
          valueId,
    );
    this.applyOption(nextOption);
    this.selectionNotice.set(
      selectionWasAdjusted
        ? 'As outras escolhas foram ajustadas para uma combinação cadastrada.'
        : '',
    );
  }

  protected selectLegacyOption(event: Event): void {
    const target = event.target;
    if (!(target instanceof HTMLSelectElement)) {
      return;
    }
    const option = this.product()?.opcoes.find((item) => item.id === target.value);
    if (option) {
      this.applyOption(option);
    }
  }

  protected selectImage(image: ProdutoCatalogoImagem): void {
    this.selectedImageId.set(image.id);
    this.imageUnavailable.set(false);
  }

  protected onImageError(): void {
    this.imageUnavailable.set(true);
  }

  protected onThumbnailError(imageId: string): void {
    this.unavailableThumbnailIds.update((ids) => new Set(ids).add(imageId));
  }

  protected thumbnailLabel(index: number): string {
    const name = this.product()?.nome ?? 'Produto';
    return `Ver foto ${index + 1} de ${name}`;
  }

  private beginLoading(id: string): void {
    this.currentProductId = id;
    this.product.set(null);
    this.loading.set(true);
    this.notFound.set(false);
    this.requestError.set(false);
    this.imageUnavailable.set(false);
    this.unavailableThumbnailIds.set(new Set());
    this.selectedOptionId.set(null);
    this.selectedValues.set({});
    this.selectedImageId.set(null);
    this.selectionNotice.set('');
    this.mensagemCarrinho.set('');
  }

  private applyResult(result: ProdutoResult): void {
    this.loading.set(false);
    if (result.failed) {
      this.notFound.set(result.error instanceof HttpErrorResponse && result.error.status === 404);
      this.carrinho.sinalizarDisponibilidade(result.id, this.notFound() ? false : null);
      this.requestError.set(!this.notFound());
      return;
    }

    this.product.set(result.product);
    this.carrinho.atualizarDisponibilidade(result.product);
    const defaultOption =
      result.product.opcoes.find((option) => option.id === result.product.opcaoPrincipalId) ??
      result.product.opcoes.find((option) => option.principal) ??
      result.product.opcoes[0];
    if (defaultOption) {
      this.applyOption(defaultOption);
    } else {
      this.selectedImageId.set(result.product.fotoPrincipal.id);
    }
  }

  private applyOption(option: ProdutoCatalogoOpcao): void {
    this.mensagemCarrinho.set('');
    this.selectedOptionId.set(option.id);
    this.selectedValues.set(
      Object.fromEntries(
        option.caracteristicas.map((item) => [item.caracteristicaId, item.valorId]),
      ),
    );
    this.selectionNotice.set('');

    const product = this.product();
    if (!product) {
      return;
    }
    const linkedImage = product.galeria.find((image) => image.opcaoIds.includes(option.id));
    const generalImage = product.galeria.find((image) => image.opcaoIds.length === 0);
    const preferredImage = linkedImage ?? generalImage ?? product.fotoPrincipal;
    this.selectedImageId.set(preferredImage.id);
    this.imageUnavailable.set(false);
  }
}

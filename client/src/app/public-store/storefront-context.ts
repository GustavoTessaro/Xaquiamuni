import { computed, inject, Injectable, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { buscarManifestoTemaLoja, resolverTemaLoja, TEMA_ORIGINAL } from './manifestos-tema-loja';

@Injectable({ providedIn: 'root' })
export class StorefrontContext {
  private readonly route = inject(ActivatedRoute, { optional: true });
  readonly preview = this.route?.snapshot.data['storePreview'] === true;
  private readonly params = this.preview && this.route
    ? toSignal(this.route.paramMap, { requireSync: true })
    : null;

  readonly previewKey = computed(() => this.params?.().get('tema') ?? null);

  private readonly publicado = signal(TEMA_ORIGINAL);
  readonly tema = computed(() => this.preview ? resolverTemaLoja(this.previewKey()) : this.publicado());
  readonly manifesto = computed(() => buscarManifestoTemaLoja(this.tema()));

  aplicarPublicado(chave: unknown): void {
    this.publicado.set(resolverTemaLoja(chave));
  }

  link(...segments: string[]): string[] {
    const key = this.previewKey();
    return key ? ['/admin/temas/previa', key, ...segments] : ['/', ...segments];
  }
}

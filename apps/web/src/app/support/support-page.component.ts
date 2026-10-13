import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  type SupportCase,
  type SupportCategory,
  type SupportClassificationReason,
  type SupportMode,
  type SupportPriority,
  type SupportState,
  supportCategories,
  supportGuid,
  supportPriorities,
  supportStates,
} from '@travel/api-client';
import { SupportStateService } from './support-state.service';

export const supportStateLabels: Record<SupportState, string> = {
  Open: 'Открыто',
  WaitingForCustomer: 'Ждём вашего ответа',
  Resolved: 'Решено',
};
export const supportCategoryLabels: Record<SupportCategory, string> = {
  Unclassified: 'Без категории',
  Payment: 'Оплата',
  Ticketing: 'Выписка билета',
  Cancellation: 'Отмена',
  Refund: 'Возврат',
  FareConditions: 'Условия тарифа',
  Ancillaries: 'Багаж и места',
  Technical: 'Техническая проблема',
  Other: 'Другое',
};
@Component({
  selector: 'app-support-page',
  standalone: true,
  imports: [FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './support-page.component.html',
  styleUrl: './support-page.component.scss',
})
export class SupportPageComponent {
  readonly state = inject(SupportStateService);
  readonly auth = this.state.auth;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  readonly title = signal('');
  readonly body = signal('');
  readonly orderId = signal<string | null>(null);
  readonly category = signal<SupportCategory>('Unclassified');
  readonly priority = signal<SupportPriority>('Normal');
  readonly draftReviewRequired = signal(false);
  private suggestionDraft: {
    caseId: string;
    revision: number;
    generation: number;
    expiresAt: number;
    edited: boolean;
  } | null = null;
  private retainedManualDraft = false;
  private manualDraftReview: { caseId: string; revision: number } | null = null;
  private triageSnapshot: SupportCase | null = null;
  readonly classificationReasonLabels: Record<SupportClassificationReason, string> = {
    Disabled: 'Подсказки выключены. Выберите категорию вручную.',
    Ready: 'Подсказка для проверки оператором; правильность не гарантируется.',
    InputTooLarge: 'Переписка не помещается в допустимый объём. Выберите категорию вручную.',
    Unavailable: 'Сервис подсказок недоступен. Продолжите вручную.',
    Uncertain:
      'Вычисление могло выполниться, но результат не подтверждён. Автоматического повтора нет. Новый явный запрос расходует лимит заново.',
    InvalidResponse: 'Ответ сервиса не прошёл проверку. Выберите категорию вручную.',
    LowConfidence: 'Недостаточно оснований для категории. Выберите её вручную.',
    IdentityMismatch: 'Версия сервиса не подтверждена. Выберите категорию вручную.',
    Busy: 'Достигнут лимит запросов подсказок. Продолжите вручную.',
  };
  readonly filterState = signal<SupportState | ''>('');
  readonly filterCategory = signal<SupportCategory | ''>('');
  readonly filterPriority = signal<SupportPriority | ''>('');
  readonly states = supportStates;
  readonly categories = supportCategories;
  readonly priorities = supportPriorities;
  readonly stateLabels = supportStateLabels;
  readonly categoryLabels = supportCategoryLabels;
  readonly mode: SupportMode = this.route.snapshot.data['supportMode'] === 'operator' ? 'operator' : 'customer';
  readonly root = this.mode === 'operator' ? '/support/operator' : '/support';
  readonly canReopen = computed(() => {
    const row = this.state.selected();
    return (
      this.mode === 'customer' &&
      row?.state === 'Resolved' &&
      row.purgeAt !== null &&
      Date.parse(row.purgeAt) > Date.now()
    );
  });
  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const routeGeneration = ++this.routeGeneration;
      this.clearDraft();
      void this.state.enter(this.mode, params.get('id')).then(() => {
        if (this.destroy.destroyed || routeGeneration !== this.routeGeneration) return;
        this.restoreOrderLink();
      });
    });
    effect(() => {
      this.state.privateGeneration();
      this.auth.identityEpoch();
      const status = this.auth.status();
      untracked(() => {
        if (status.kind !== 'authenticated' || this.lastGeneration !== this.state.privateGeneration()) {
          this.clearDraft();
          this.lastGeneration = this.state.privateGeneration();
        }
      });
    });
    effect(() => {
      const row = this.state.selected();
      this.state.classificationGeneration();
      this.state.suggestion();
      untracked(() => {
        const hadSuggestionDraft = this.suggestionDraft !== null;
        this.validateSuggestionDraft();
        if (
          row &&
          row !== this.triageSnapshot &&
          !this.suggestionDraft &&
          !this.retainedManualDraft &&
          !hadSuggestionDraft
        ) {
          this.category.set(row.category);
          this.priority.set(row.priority);
        }
        this.triageSnapshot = row;
      });
    });
    this.destroy.onDestroy(() => {
      this.routeGeneration++;
      this.clearDraft();
      this.state.leave();
    });
  }
  private lastGeneration = -1;
  private routeGeneration = 0;
  private restoreOrderLink(): void {
    this.lastGeneration = this.state.privateGeneration();
    const raw =
      this.mode === 'customer' && this.state.caseId() === null
        ? this.route.snapshot.queryParamMap.get('orderId')
        : null;
    try {
      this.orderId.set(raw === null ? null : supportGuid(raw));
    } catch {
      this.state.error.set('Неверная ссылка на заказ. Вы можете убрать её и создать обращение без заказа.');
    }
  }
  private clearDraft(): void {
    this.triageSnapshot = null;
    this.suggestionDraft = null;
    this.retainedManualDraft = false;
    this.manualDraftReview = null;
    this.draftReviewRequired.set(false);
    this.title.set('');
    this.body.set('');
    this.orderId.set(null);
    this.category.set('Unclassified');
    this.priority.set('Normal');
  }
  async login(): Promise<void> {
    const generation = this.routeGeneration;
    let order = this.orderId();
    if (order === null && this.mode === 'customer' && this.state.caseId() === null) {
      try {
        const raw = this.route.snapshot.queryParamMap.get('orderId');
        order = raw === null ? null : supportGuid(raw);
      } catch {
        order = null;
      }
    }
    const path =
      this.root +
      (this.state.caseId()
        ? '/cases/' + this.state.caseId()
        : this.mode === 'customer' && order
          ? '?orderId=' + order
          : '');
    if ((await this.auth.beginLogin(path)) && !this.destroy.destroyed && generation === this.routeGeneration) {
      await this.state.enter(this.mode, this.state.caseId());
      if (!this.destroy.destroyed && generation === this.routeGeneration) this.restoreOrderLink();
    }
  }
  async unlinkOrder(): Promise<void> {
    this.orderId.set(null);
    await this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { orderId: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
  async logout(): Promise<void> {
    this.clearDraft();
    this.state.leave();
    await this.auth.logout();
  }
  async create(): Promise<void> {
    const generation = this.routeGeneration;
    const id = await this.state.create(this.title(), this.body(), this.orderId());
    if (id && generation === this.routeGeneration && !this.destroy.destroyed) {
      this.clearDraft();
      await this.router.navigate([this.root, 'cases', id]);
    }
  }
  async createWithoutOrder(): Promise<void> {
    const generation = this.routeGeneration;
    const id = await this.state.createWithoutOrder(this.title(), this.body());
    this.orderId.set(null);
    if (id && generation === this.routeGeneration && !this.destroy.destroyed) {
      this.clearDraft();
      await this.router.navigate([this.root, 'cases', id]);
    }
  }
  async reply(reopen: boolean): Promise<void> {
    const generation = this.routeGeneration;
    if ((await this.state.reply(this.body(), reopen)) && generation === this.routeGeneration && !this.destroy.destroyed)
      this.body.set('');
  }
  prefillSuggestion(): void {
    const suggestion = this.state.suggestion(),
      row = this.state.selected();
    if (
      !suggestion ||
      !row ||
      suggestion.status !== 'Candidate' ||
      !suggestion.category ||
      this.state.writeBlocked() ||
      Date.parse(suggestion.expiresAt) <= Date.now()
    )
      return;
    this.category.set(suggestion.category);
    this.draftReviewRequired.set(false);
    this.retainedManualDraft = false;
    this.manualDraftReview = null;
    this.suggestionDraft = {
      caseId: row.caseId,
      revision: row.revision,
      generation: this.state.classificationGeneration(),
      expiresAt: Date.parse(suggestion.expiresAt),
      edited: this.priority() !== row.priority,
    };
  }
  private validateSuggestionDraft(): void {
    const binding = this.suggestionDraft,
      row = this.state.selected();
    if (
      this.retainedManualDraft &&
      (!row ||
        !this.manualDraftReview ||
        this.manualDraftReview.caseId !== row.caseId ||
        this.manualDraftReview.revision !== row.revision)
    ) {
      this.manualDraftReview = null;
      this.draftReviewRequired.set(true);
    }
    if (
      !binding ||
      (this.state.suggestion() &&
        binding.generation === this.state.classificationGeneration() &&
        row?.caseId === binding.caseId &&
        row.revision === binding.revision &&
        binding.expiresAt > Date.now())
    )
      return;
    this.suggestionDraft = null;
    if (binding.edited && row?.caseId === binding.caseId) {
      this.retainedManualDraft = true;
      this.manualDraftReview = null;
      this.draftReviewRequired.set(true);
    } else {
      this.retainedManualDraft = false;
      this.manualDraftReview = null;
      this.category.set(row?.category ?? 'Unclassified');
      this.priority.set(row?.priority ?? 'Normal');
    }
  }
  editCategory(value: SupportCategory): void {
    this.validateSuggestionDraft();
    this.category.set(value);
    if (this.suggestionDraft) this.suggestionDraft.edited = true;
  }
  editPriority(value: SupportPriority): void {
    this.validateSuggestionDraft();
    this.priority.set(value);
    if (this.suggestionDraft) this.suggestionDraft.edited = true;
  }
  reviewDraft(): void {
    this.validateSuggestionDraft();
    const row = this.state.selected();
    if (!row || this.state.writeBlocked()) return;
    this.manualDraftReview = { caseId: row.caseId, revision: row.revision };
    this.draftReviewRequired.set(false);
  }
  async saveTriage(): Promise<void> {
    this.validateSuggestionDraft();
    if (this.draftReviewRequired() || this.state.writeBlocked()) return;
    const generation = this.routeGeneration;
    if (await this.state.triage(this.category(), this.priority())) {
      if (generation !== this.routeGeneration || this.destroy.destroyed) return;
      this.suggestionDraft = null;
      this.retainedManualDraft = false;
      this.manualDraftReview = null;
      this.draftReviewRequired.set(false);
      const row = this.state.selected();
      if (row) {
        this.category.set(row.category);
        this.priority.set(row.priority);
      }
    }
  }
  async filter(): Promise<void> {
    await this.state.refresh({
      ...(this.filterState() ? { state: this.filterState() as SupportState } : {}),
      ...(this.filterCategory() ? { category: this.filterCategory() as SupportCategory } : {}),
      ...(this.filterPriority() ? { priority: this.filterPriority() as SupportPriority } : {}),
    });
  }
  stateLabel(state: SupportState): string {
    return state === 'WaitingForCustomer' && this.mode === 'operator' ? 'Ждём ответа клиента' : this.stateLabels[state];
  }
  formatTime(value: string): string {
    return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
  }
}

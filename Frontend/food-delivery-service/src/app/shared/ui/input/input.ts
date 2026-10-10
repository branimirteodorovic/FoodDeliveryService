import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, NgControl, Validators } from '@angular/forms';

export type InputType = 'text' | 'email' | 'tel' | 'password' | 'search' | 'url' | 'number';

// Fallback copy for the built-in validators; a control can override any of these per key.
const DEFAULT_MESSAGES: Record<string, (error: unknown) => string> = {
  required: () => 'This field is required.',
  email: () => 'Enter a valid email address.',
  minlength: (e) => `Use at least ${(e as { requiredLength: number }).requiredLength} characters.`,
  maxlength: (e) => `Use at most ${(e as { requiredLength: number }).requiredLength} characters.`,
  min: (e) => `Must be ${(e as { min: number }).min} or more.`,
  max: (e) => `Must be ${(e as { max: number }).max} or less.`,
  pattern: () => 'This value is not in the expected format.',
};

let nextId = 0;

/**
 * Spec: Design sheet 04 · Inputs.
 * A real <label for> + real <input>, usable with formControl / formControlName / ngModel.
 * Errors appear only after the field is touched (blur), never on first keystroke, and always
 * as icon + text so colour is never the only signal. The message slot is always rendered
 * so an error appearing does not push the form down.
 */
@Component({
  selector: 'app-input',
  styleUrl: './input.css',
  templateUrl: './input.html',
})
export class Input implements ControlValueAccessor, OnInit {
  label = input.required<string>();
  hint = input<string>();
  type = input<InputType>('text');
  placeholder = input<string>();
  autocomplete = input<string>();
  inputmode = input<'text' | 'numeric' | 'decimal' | 'email' | 'tel' | 'url' | 'search'>();
  maxlength = input<number>();
  /** Per-validator-key copy, e.g. { required: 'Enter your street.' }. A string error value wins over both. */
  errorMessages = input<Record<string, string>>({});

  protected readonly id = `app-input-${nextId++}`;
  protected readonly messageId = `${this.id}-msg`;

  protected readonly value = signal('');
  protected readonly disabled = signal(false);

  // Reactive-forms state is not signal-based, so every control event bumps this to re-run the computeds below.
  private readonly tick = signal(0);

  private readonly ngControl = inject(NgControl, { self: true, optional: true });
  private readonly destroyRef = inject(DestroyRef);

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  constructor() {
    // Providing NG_VALUE_ACCESSOR instead would create a circular dependency with NgControl.
    if (this.ngControl) this.ngControl.valueAccessor = this;
  }

  ngOnInit(): void {
    this.ngControl?.control?.events
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.tick.update((n) => n + 1));
  }

  /** No required validator means the field says "optional" instead of marking the others with asterisks. */
  protected readonly optional = computed(() => {
    this.tick();
    const control = this.ngControl?.control;
    return !!control && !control.hasValidator(Validators.required);
  });

  protected readonly showError = computed(() => {
    this.tick();
    const control = this.ngControl?.control;
    return !!control && control.invalid && control.touched;
  });

  protected readonly showValid = computed(() => {
    this.tick();
    const control = this.ngControl?.control;
    return !!control && control.valid && control.touched && this.value() !== '';
  });

  protected readonly errorText = computed(() => {
    this.tick();
    const errors = this.ngControl?.control?.errors;
    if (!errors) return '';
    const key = Object.keys(errors)[0];
    const error = errors[key];
    if (typeof error === 'string') return error;
    return this.errorMessages()[key] ?? DEFAULT_MESSAGES[key]?.(error) ?? 'This value is not valid.';
  });

  protected onInput(event: Event): void {
    const next = (event.target as HTMLInputElement).value;
    this.value.set(next);
    this.onChange(next);
  }

  protected onBlur(): void {
    this.onTouched();
    this.tick.update((n) => n + 1);
  }

  writeValue(value: string | null): void {
    this.value.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }
}

import { Component, computed, input } from '@angular/core';

export type SpinnerSize = 'sm' | 'md' | 'lg';

/**
 * Spec: Design sheet 06 · Feedback (spinner), sheet 07 (motion).
 * A 270° arc in --accent, 720ms per turn, for waits with no known shape: a payment, a submit.
 * Where the shape is known, use a skeleton instead.
 *
 * - sm 16 inline · md 24 button · lg 36 panel.
 * - A blocking wait should say what it is waiting for: pass `label` and, when sighted users need it,
 *   `showLabel`. The label is always present for assistive tech either way (role="status").
 * - `delay`: nothing is drawn until this many ms have passed, so a fast response doesn't flash a
 *   spinner (the spec says under 300ms nothing is shown). Defaults to 0 for already-deliberate spinners.
 * - Reduced motion is the global rule in tokens.css (slows .spinner to 2000ms); no per-component check.
 */
@Component({
  selector: 'app-spinner',
  styleUrl: './spinner.css',
  templateUrl: './spinner.html',
  host: {
    role: 'status',
    '[style.--spinner-delay]': 'delay() + "ms"',
  },
})
export class Spinner {
  size = input<SpinnerSize>('md');
  label = input('Loading');
  showLabel = input(false);
  delay = input(0);

  protected readonly spinnerClasses = computed(() => `spinner spinner--${this.size()}`);
  // the panel size uses a slightly thinner stroke so the larger arc doesn't look heavy
  protected readonly strokeWidth = computed(() => (this.size() === 'lg' ? 2.2 : 2.5));
}

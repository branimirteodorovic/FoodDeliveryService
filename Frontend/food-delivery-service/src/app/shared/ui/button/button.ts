import { booleanAttribute, Component, computed, input } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-button',
  styleUrl: './button.css',
  templateUrl: './button.html',
  host: {
    '[class.is-block]': 'fullWidth()',
  },
})
export class Button {
  variant = input<ButtonVariant>('primary');
  size = input<ButtonSize>('md');
  // booleanAttribute so the bare attribute form works: without a transform <app-button disabled>
  // passes the string '', which is falsy, and the button would render fully enabled.
  disabled = input(false, { transform: booleanAttribute });
  loading = input(false, { transform: booleanAttribute });
  fullWidth = input(false, { transform: booleanAttribute });
  type = input<'button' | 'submit' | 'reset'>('button');

  // Loading wins over disabled: a loading button keeps its normal colours (sheet 03), while a
  // disabled one gets the flat treatment. Both set the native `disabled` attribute in the template.
  classes = computed(() => {
    const parts = ['btn', `btn--${this.variant()}`, `btn--${this.size()}`];
    if (this.loading()) parts.push('btn--loading');
    else if (this.disabled()) parts.push('btn--disabled');
    if (this.fullWidth()) parts.push('btn--block');
    return parts.join(' ');
  });
}

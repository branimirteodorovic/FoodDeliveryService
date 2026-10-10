import { booleanAttribute, Component, computed, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';
export type BadgeSize = 'sm' | 'md';
export type BadgeShape = 'pill' | 'tag';

@Component({
  selector: 'app-badge',
  styleUrl: './badge.css',
  templateUrl: './badge.html',
})
export class Badge {
  tone = input<BadgeTone>('neutral');
  size = input<BadgeSize>('md');
  shape = input<BadgeShape>('pill');
  dashed = input(false, { transform: booleanAttribute });

  protected readonly classes = computed(() => {
    const parts = [
      'badge',
      `badge--${this.tone()}`,
      `badge--${this.size()}`,
      `badge--${this.shape()}`,
    ];
    if (this.dashed()) parts.push('badge--dashed');
    return parts.join(' ');
  });
}

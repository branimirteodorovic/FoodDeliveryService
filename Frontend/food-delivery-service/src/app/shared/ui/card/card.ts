import { booleanAttribute, Component, computed, input } from '@angular/core';

export type CardPadding = 'none' | 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-card',
  styleUrl: './card.css',
  templateUrl: './card.html',
})
export class Card {
  /** Body padding. Use 'none' for edge-to-edge content such as a table or an image. */
  padding = input<CardPadding>('md');
  /** One shadow step (--sh-sm), for cards that genuinely sit above the page. */
  elevated = input(false, { transform: booleanAttribute });

  protected readonly bodyClasses = computed(() => `card__body card__body--${this.padding()}`);
}

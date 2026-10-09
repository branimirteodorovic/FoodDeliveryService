import { Component, signal } from '@angular/core';
import { Button } from './shared/ui/button/button';

@Component({
  imports: [Button],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly title = signal('food-delivery-service');
}

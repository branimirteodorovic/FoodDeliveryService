import { Component, signal } from '@angular/core';
import { Badge } from './shared/ui/badge/badge';
import { Button } from './shared/ui/button/button';
import { Card } from './shared/ui/card/card';
import { Spinner } from './shared/ui/spinner/spinner';

@Component({
  imports: [Badge, Button, Card, Spinner],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly title = signal('food-delivery-service');
}

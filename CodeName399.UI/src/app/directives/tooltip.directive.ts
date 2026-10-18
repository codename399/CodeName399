import {
  Directive,
  ElementRef,
  HostListener,
  Input,
  OnDestroy,
  Renderer2,
} from '@angular/core';

@Directive({
  selector: '[appTooltip]',
  standalone: true,
})
export class TooltipDirective implements OnDestroy {
  @Input('appTooltip') text = '';

  @Input() tooltipPosition: 'top' | 'bottom' | 'left' | 'right' = 'left';

  private tooltip?: HTMLElement;

  constructor(
    private element: ElementRef,
    private renderer: Renderer2,
  ) {}

  @HostListener('click', ['$event'])
  toggle(event: Event) {
    event.stopPropagation();

    if (this.tooltip) {
      this.destroyTooltip();
      return;
    }

    this.createTooltip();
  }

  @HostListener('document:click')
  close() {
    this.destroyTooltip();
  }

  private createTooltip(): void {
    const tooltip = this.renderer.createElement('div') as HTMLDivElement;

    this.tooltip = tooltip;

    this.renderer.addClass(tooltip, 'app-tooltip');
    this.renderer.addClass(tooltip, this.tooltipPosition);

    tooltip.innerText = this.text;

    this.renderer.appendChild(document.body, tooltip);

    const hostRect = this.element.nativeElement.getBoundingClientRect();
    const tooltipRect = tooltip.getBoundingClientRect();

    let left = 0;
    let top = 0;

    switch (this.tooltipPosition) {
      case 'left':
        left = hostRect.left - tooltipRect.width - 10;
        top = hostRect.top + hostRect.height / 2 - tooltipRect.height / 2;
        break;

      case 'right':
        left = hostRect.right + 10;
        top = hostRect.top + hostRect.height / 2 - tooltipRect.height / 2;
        break;

      case 'top':
        left = hostRect.left + hostRect.width / 2 - tooltipRect.width / 2;
        top = hostRect.top - tooltipRect.height - 10;
        break;

      case 'bottom':
        left = hostRect.left + hostRect.width / 2 - tooltipRect.width / 2;
        top = hostRect.bottom + 10;
        break;
    }

    this.renderer.setStyle(tooltip, 'left', `${left}px`);
    this.renderer.setStyle(tooltip, 'top', `${top}px`);
  }

  private destroyTooltip() {
    if (!this.tooltip) {
      return;
    }

    this.renderer.removeChild(document.body, this.tooltip);
    this.tooltip = undefined;
  }

  ngOnDestroy() {
    this.destroyTooltip();
  }
}

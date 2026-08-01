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
  @Input('appTooltip') tooltipText = '';

  private tooltipEl: HTMLElement | null = null;

  constructor(
    private readonly elementRef: ElementRef<HTMLElement>,
    private readonly renderer: Renderer2,
  ) {}

  @HostListener('mouseenter')
  onMouseEnter(): void {
    this.show();
  }

  @HostListener('mouseleave')
  onMouseLeave(): void {
    this.hide();
  }

  @HostListener('focus')
  onFocus(): void {
    this.show();
  }

  @HostListener('blur')
  onBlur(): void {
    this.hide();
  }

  @HostListener('click')
  onClick(): void {
    if (this.isTouchDevice()) {
      this.toggle();
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    const target = event.target as Node | null;

    if (!target || !this.elementRef.nativeElement.contains(target)) {
      this.hide();
    }
  }

  ngOnDestroy(): void {
    this.hide();
  }

  private toggle(): void {
    if (this.tooltipEl) {
      this.hide();
      return;
    }

    this.show();
  }

  private show(): void {
    if (!this.tooltipText || this.tooltipEl) {
      return;
    }

    this.tooltipEl = this.renderer.createElement('div');
    this.renderer.addClass(this.tooltipEl, 'tooltip-panel');
    this.renderer.setAttribute(this.tooltipEl, 'role', 'tooltip');
    this.renderer.setProperty(this.tooltipEl, 'textContent', this.tooltipText);
    this.renderer.appendChild(document.body, this.tooltipEl);

    this.positionTooltip();
  }

  private hide(): void {
    if (!this.tooltipEl) {
      return;
    }

    this.renderer.removeChild(document.body, this.tooltipEl);
    this.tooltipEl = null;
  }

  private positionTooltip(): void {
    if (!this.tooltipEl) {
      return;
    }

    const hostRect = this.elementRef.nativeElement.getBoundingClientRect();
    const tooltipRect = this.tooltipEl.getBoundingClientRect();

    let top = hostRect.bottom + 8;
    let left = hostRect.left + hostRect.width / 2 - tooltipRect.width / 2;

    if (left < 8) {
      left = 8;
    }

    const maxLeft = window.innerWidth - tooltipRect.width - 8;

    if (left > maxLeft) {
      left = maxLeft;
    }

    const maxTop = window.innerHeight - tooltipRect.height - 8;

    if (top > maxTop) {
      top = hostRect.top - tooltipRect.height - 8;
    }

    this.renderer.setStyle(this.tooltipEl, 'top', `${Math.max(8, top)}px`);
    this.renderer.setStyle(this.tooltipEl, 'left', `${Math.max(8, left)}px`);
  }

  private isTouchDevice(): boolean {
    return window.matchMedia('(pointer: coarse)').matches;
  }
}

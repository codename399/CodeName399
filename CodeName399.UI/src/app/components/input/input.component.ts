import { Component, forwardRef, input, model, OnInit, Optional, Self } from '@angular/core';
import { InputType } from '../../models/enums/input-type';
import { getErrorMessage, isInvalid } from '../../../validators/field-validator';
import { AbstractControl, ControlValueAccessor, NG_VALUE_ACCESSOR, NgControl } from '@angular/forms';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-input',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './input.component.html',
  styleUrl: './input.component.css'
})
export class InputComponent<I> implements OnInit, ControlValueAccessor {
  InputType = InputType;
  type = input.required<InputType>();
  placeholder = input<string>("");
  label = input<string>("");
  min = input<number>();
  max = input<number>();
  maxlength = input<number>();
  options = input<Record<string, any>[]>([]);
  textField = input<string>("name");
  valueField = input<string>("id");
  default = input<string>();
  disabled = model<boolean>(false);

  id: string = "";
  value: any = "";

  get getErrorMessage() {
    return getErrorMessage;
  }

  get isInvalid() {
    return isInvalid;
  }

  #onChange = (value: any) => { };
  #onTouched = () => { };

  constructor(@Optional() @Self() public ngControl: NgControl) {
    if (ngControl) {
      // tell Angular this component implements CVA
      ngControl.valueAccessor = this;
    }
  }

  ngOnInit(): void {
    this.id = `${this.type.toString().toLowerCase}_${this.label.toString().toLowerCase().replaceAll(" ", "_")}`;
  }

  writeValue(value: any): void {
    this.value = value || '';
  }

  registerOnChange(fn: any): void {
    this.#onChange = fn;
  }

  registerOnTouched(fn: any): void {
    this.#onTouched = fn;
  }

  setDisabledState?(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  handleInput(event: Event): void {
    const element = (event.target as HTMLInputElement)
    const val = element.type == "checkbox" ? element.checked : element.value;
    this.value = val;
    this.#onChange(val);
  }

  handleBlur(): void {
    this.#onTouched();
  }

  onSelect(event: Event) {
    const selectedValue = (event.target as HTMLSelectElement).value;
    this.value = selectedValue;
    this.#onChange(this.value);
    this.#onTouched();
  }

  // 🧠 Helper for validation state
  get control(): AbstractControl | null {
    return this.ngControl?.control;
  }

  get showError(): boolean {
    const c = this.control;
    return !!(c && c.invalid && (c.dirty || c.touched));
  }
}

import { Component, input, model, output } from '@angular/core';

@Component({
  selector: 'app-dialog',
  standalone: true,
  imports: [],
  templateUrl: './dialog.component.html',
  styleUrl: './dialog.component.css'
})
export class DialogComponent {
  title = input<string>("Confirmation!!");
  message = input<string>("Are you sure?");
  confirmationText = input<string>("Confirm");
  cancelText = input<string>("Cancel");
  showConfirmation = input<boolean>(true);
  showCancel = input<boolean>(true);

  confirm = output();

  showDialog: boolean = false;

  onConfirm() {
    this.confirm.emit();
    this.onCancel();
  }

  onCancel() {
    this.showDialog = false;
  }

  openDialog() {
    this.showDialog = true;
  }
}

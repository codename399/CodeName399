import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'camelCase',
  standalone: true
})
export class CamelCasePipe implements PipeTransform {
  transform(value: string): string {
    if (!value) return '';
    return value
      .replace(/(?:^\w|[A-Z]|\b\w|\s+)/g, (match, index) => {
        if (+match === 0) return ''; // remove spaces
        return index === 0 ? match.toLowerCase() : match.toUpperCase();
      });
  }
}

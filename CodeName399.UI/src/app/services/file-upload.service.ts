import { inject, Injectable, signal } from "@angular/core";
import { Config } from "../../assets/environments/config";

@Injectable({
    providedIn: "root"
})
export class FileUploadService {
    #config = inject(Config)
    #url = signal<string>(this.#config.profilePictureUrl);

    get url() {
        return this.#url();
    }

    set url(value: string) {
        this.#url.set(value);
    }

    onFileSelected(event: any) {
        const file = (event.target as HTMLInputElement)?.files?.[0];
        if (file) {
            const reader = new FileReader();
            reader.onload = () => {
                this.url = reader.result as string;
            };
            reader.readAsDataURL(file);
        }
    }
}
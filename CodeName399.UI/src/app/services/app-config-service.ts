import { inject, Injectable } from "@angular/core";
import { Config } from "../../assets/environments/config";
import { HttpBackend, HttpClient } from "@angular/common/http";
import { CONFIG_FILE_PATH } from "../../injectors/common-injector";
import { ToastService } from "./toast.service";

@Injectable(
    {
        providedIn: "root"
    }
)
export class ConfigService {
    #config!: Config;
    #httpBackend = inject(HttpBackend);
    #configFilePath = inject(CONFIG_FILE_PATH);
    #toastService = inject(ToastService);

    get value() {
        return this.#config;
    }

    load() {
        let httpClient = new HttpClient(this.#httpBackend);

        return new Promise<void>((resolve, reject) => {
            httpClient.get<Config>(this.#configFilePath)
                .toPromise()
                .then((config) => {
                    if (config) {
                        this.#config = config;
                    }
                    else {
                        this.#toastService.error("Configuration not found.");
                    }

                    resolve();
                })
                .catch(() => {
                    reject(
                        this.#toastService.error("Could not show load config file.")
                    )
                })
        });
    }
}
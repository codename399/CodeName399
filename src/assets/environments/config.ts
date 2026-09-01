import { IEnvironment } from "./environment-type";

export class Config implements IEnvironment {
    pageSize!: number;
    profilePictureUrl!: string;
    authenticationBaseURL!: string;
    baseURL!: string;
    equityTradingBaseURL!: string;
    futuresTradingBaseURL!: string;
    optionsTradingBaseURL!: string;
    toast_delay!: number;
    loadGridImages!: boolean;
}
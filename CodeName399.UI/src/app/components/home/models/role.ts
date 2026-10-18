import { Audit } from "../../../models/audit";

export interface Role extends Audit {
    id: string | null;
    name: string;
}
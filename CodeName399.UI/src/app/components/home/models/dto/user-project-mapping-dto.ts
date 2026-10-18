import { Audit } from "../../../../models/audit";

export interface UserProjectMappingDto extends Audit {
    userId: string;
    projectIds?: string[];
}
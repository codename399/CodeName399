import { Audit } from "../../../models/audit";

export interface Project extends Audit{
  name: string;
  description: string | null;
  imageUrl: string | null;
  route: string | null;
  navigationText: string | null;
  isAdmin: boolean;
}

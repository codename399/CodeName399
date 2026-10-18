import { Audit } from "../../../models/audit";

export interface User extends Audit {
  name: string;
  username: string;
  password: string;
  emailId: string;
  contactNumber: string;
  roleId: string | null;
  profilePicture: string | null;
}

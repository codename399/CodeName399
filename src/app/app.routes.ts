import { Routes } from '@angular/router';
import { AuthGuard } from '../route-guards/auth-guard';
import { NoAuthGuard } from '../route-guards/no-auth-guard';
import { RegisterResolver } from './components/authentication/resolvers/register-resolver';
import { DashboardResolver } from './components/home/resolvers/dashboard-resolver';
import { ProjectResolver } from './components/home/resolvers/project-resolver';
import { RoleResolver } from './components/home/resolvers/role-resolver';
import { UserResolver } from './components/home/resolvers/user-resolver';
import { UserProjectMappingResolver } from './components/home/resolvers/user-project-mapping-resolver';
import { Constants } from '../constants';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'home',
    pathMatch: 'full',
  },
  {
    path: 'login',
    loadComponent: () =>
      import(
        '../app/components/authentication/components/login-component/login-component'
      ).then((c) => c.LoginComponent),
    canActivate: [NoAuthGuard],
  },
  {
    path: 'oauth-callback',
    loadComponent: () =>
      import('../app/components/authentication/components/oauth-callback/oauth-callback.component')
        .then((c) => c.OAuthCallbackComponent),
    canActivate: [NoAuthGuard],
  },
  {
    path: 'otp-login',
    loadComponent: () =>
      import('../app/components/authentication/components/otp-login/otp-login.component')
        .then((c) => c.OtpLoginComponent),
    canActivate: [NoAuthGuard],
  },
  {
    path: 'forgot-password',
    loadComponent: () =>
      import('../app/components/authentication/components/forgot-password/forgot-password.component')
        .then((c) => c.ForgotPasswordComponent),
    canActivate: [NoAuthGuard],
  },
  {
    path: 'logout',
    loadComponent: () =>
      import(
        '../app/components/authentication/components/logout-component/logout-component'
      ).then((c) => c.LogoutComponent),
    canActivate: [AuthGuard],
  },
  {
    path: 'home',
    loadComponent: () =>
      import('../app/components/home/components/home/home.component').then(
        (c) => c.HomeComponent
      ),
    canActivate: [AuthGuard],
    children: [
      {
        path: '',
        redirectTo: 'dashboard',
        pathMatch: 'full',
      },
      {
        path: 'change-password',
        loadComponent: () =>
          import(
            '../app/components/home/components/change-password/change-password.component'
          ).then((c) => c.ChangePasswordComponent),
        canActivate: [AuthGuard],
      },
      {
        path: 'register/:id',
        loadComponent: () =>
          import(
            '../app/components/authentication/components/register/register.component'
          ).then((c) => c.RegisterComponent),
        canActivate: [AuthGuard],
        resolve: {
          users: RegisterResolver,
        },
      },
      {
        path: 'dashboard',
        loadComponent: () =>
          import(
            '../app/components/home/components/dashboard/dashboard.component'
          ).then((c) => c.DashboardComponent),
        canActivate: [AuthGuard],
        resolve: {
          projects: DashboardResolver,
        },
      },
      {
        path: 'manage-roles',
        loadComponent: () =>
          import('../app/components/home/components/role/role.component').then(
            (c) => c.RoleComponent
          ),
        canActivate: [AuthGuard],
        resolve: {
          pagedResponse: RoleResolver,
        },
      },
      {
        path: 'manage-users',
        loadComponent: () =>
          import('../app/components/home/components/user/user.component').then(
            (c) => c.UserComponent
          ),
        canActivate: [AuthGuard],
        resolve: {
          pagedResponse: UserResolver,
        },
      },
      {
        path: 'manage-projects',
        loadComponent: () =>
          import(
            '../app/components/home/components/project/project.component'
          ).then((c) => c.ProjectComponent),
        canActivate: [AuthGuard],
        resolve: {
          pagedResponse: ProjectResolver,
        },
      },
      {
        path: 'map-user-projects',
        loadComponent: () =>
          import(
            '../app/components/home/components/user-project-mapping/user-project-mapping.component'
          ).then((c) => c.UserProjectMappingComponent),
        canActivate: [AuthGuard],
        resolve: {
          usersandprojects: UserProjectMappingResolver,
        },
      },
      {
        path: 'kuber399',
        loadComponent: () =>
          import(
            './components/home/components/kuber399/kuber399.component'
          ).then((c) => c.Kuber399Component),
        data: { projectName: Constants.angelOne }
      },
      {
        path: 'trading-settings',
        loadComponent: () =>
          import(
            './components/home/components/kuber399/trading-settings/trading-settings.component'
          ).then((c) => c.TradingSettingsComponent),
        data: { projectName: Constants.angelOne }
      }
    ],
  },
  {
    path: 'register',
    loadComponent: () =>
      import(
        '../app/components/authentication/components/register/register.component'
      ).then((c) => c.RegisterComponent),
    canActivate: [NoAuthGuard],
  },
  {
    path: '**',
    redirectTo: 'home',
    pathMatch: 'full',
  },
];

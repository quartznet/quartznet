declare module '*.html' {
  import { IContainer, PartialBindableDefinition } from 'aurelia';
  export const name: string;
  export const template: string;
  export default template;
  export const dependencies: string[];
  export const containerless: boolean | undefined;
  export const bindables: Record<string, PartialBindableDefinition>;
  export const shadowOptions: { mode: 'open' | 'closed' } | undefined;
  export function register(container: IContainer): void;
}

declare module '*.css';

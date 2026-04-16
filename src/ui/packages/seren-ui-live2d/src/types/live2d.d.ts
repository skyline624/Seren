declare module 'pixi-live2d-display' {
  import type { Container } from 'pixi.js'

  export class Live2DModel extends Container {
    motion(group: string, index: number): void
    expression(name: string | number): void
    focus(x: number, y: number): void
    tap(x: number, y: number): void
    static from(source: string | object): Promise<Live2DModel>
  }

  export class Live2DLoader {
    static register(): void
  }
}
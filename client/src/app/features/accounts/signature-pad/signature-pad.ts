import { Component, ElementRef, ViewChild, AfterViewInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

// The logical size of the signing area. The canvas is drawn at SCALE times
// that so a signature stays crisp on a high-density screen, and the exported
// image (1000×320) is within what the server accepts (SignaturePng).
const WIDTH = 500;
const HEIGHT = 160;
const SCALE = 2;

// How much ink (in canvas pixels, across all strokes) counts as a signature
// rather than an accidental tap.
const MIN_INK = 40 * SCALE;

interface Point {
  x: number;
  y: number;
}

// A box to sign in with a finger, stylus or mouse. It doesn't upload
// anything itself: the parent asks for the drawing with toDataUrl() when the
// person confirms, and uses hasInk() to decide whether they've signed.
@Component({
  selector: 'app-signature-pad',
  imports: [MatButtonModule],
  templateUrl: './signature-pad.html',
  styleUrl: './signature-pad.scss',
})
export class SignaturePad implements AfterViewInit {
  @ViewChild('canvas', { static: true }) private readonly canvasRef!: ElementRef<HTMLCanvasElement>;

  readonly hasInk = signal(false);

  private context!: CanvasRenderingContext2D;
  private drawing = false;
  private last: Point | null = null;
  private lastMid: Point | null = null;
  private inkLength = 0;

  ngAfterViewInit(): void {
    const canvas = this.canvasRef.nativeElement;
    canvas.width = WIDTH * SCALE;
    canvas.height = HEIGHT * SCALE;

    this.context = canvas.getContext('2d')!;
    this.context.lineCap = 'round';
    this.context.lineJoin = 'round';
    this.context.lineWidth = 2.5 * SCALE;
    this.context.strokeStyle = '#111';
    this.context.fillStyle = '#111';
  }

  protected start(event: PointerEvent): void {
    // Stops the page scrolling or selecting text while signing on a touchscreen.
    event.preventDefault();
    this.canvasRef.nativeElement.setPointerCapture(event.pointerId);

    const point = this.point(event);
    this.drawing = true;
    this.last = point;
    this.lastMid = point;

    // A dot, so a single tap leaves a mark like a pen touching paper.
    this.context.beginPath();
    this.context.arc(point.x, point.y, this.context.lineWidth / 2, 0, Math.PI * 2);
    this.context.fill();
  }

  protected move(event: PointerEvent): void {
    if (!this.drawing) {
      return;
    }
    event.preventDefault();

    // Browsers batch fast movement; using the batched points keeps a quick
    // stroke smooth instead of a series of straight segments.
    const events = event.getCoalescedEvents?.() ?? [];
    for (const e of events.length > 0 ? events : [event]) {
      this.extendTo(this.point(e));
    }
  }

  protected end(): void {
    // The curve so far stops at the midpoint of the last segment; finish the
    // line to where the pen actually lifted.
    if (this.drawing && this.last && this.lastMid) {
      this.context.beginPath();
      this.context.moveTo(this.lastMid.x, this.lastMid.y);
      this.context.lineTo(this.last.x, this.last.y);
      this.context.stroke();
    }

    this.drawing = false;
    this.last = null;
    this.lastMid = null;
  }

  clear(): void {
    const canvas = this.canvasRef.nativeElement;
    this.context.clearRect(0, 0, canvas.width, canvas.height);
    this.inkLength = 0;
    this.hasInk.set(false);
    this.end();
  }

  // The drawing as a PNG data URL, on a white background so it reads like ink
  // on paper whatever theme it's later shown in.
  toDataUrl(): string {
    const canvas = this.canvasRef.nativeElement;
    const out = document.createElement('canvas');
    out.width = canvas.width;
    out.height = canvas.height;

    const context = out.getContext('2d')!;
    context.fillStyle = '#fff';
    context.fillRect(0, 0, out.width, out.height);
    context.drawImage(canvas, 0, 0);
    return out.toDataURL('image/png');
  }

  private extendTo(to: Point): void {
    if (!this.last || !this.lastMid) {
      return;
    }

    // A curve through the midpoints of successive points, which rounds off
    // the corners a plain polyline would have.
    const mid = { x: (this.last.x + to.x) / 2, y: (this.last.y + to.y) / 2 };
    this.context.beginPath();
    this.context.moveTo(this.lastMid.x, this.lastMid.y);
    this.context.quadraticCurveTo(this.last.x, this.last.y, mid.x, mid.y);
    this.context.stroke();

    this.inkLength += Math.hypot(to.x - this.last.x, to.y - this.last.y);
    if (this.inkLength >= MIN_INK && !this.hasInk()) {
      this.hasInk.set(true);
    }

    this.last = to;
    this.lastMid = mid;
  }

  // Screen coordinates to canvas pixels. The canvas is shown at whatever
  // width fits the screen, so this scales by the actual rendered size.
  private point(event: PointerEvent): Point {
    const canvas = this.canvasRef.nativeElement;
    const rect = canvas.getBoundingClientRect();
    return {
      x: (event.clientX - rect.left) * (canvas.width / rect.width),
      y: (event.clientY - rect.top) * (canvas.height / rect.height),
    };
  }
}

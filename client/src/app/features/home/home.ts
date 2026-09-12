import { Component } from '@angular/core';

@Component({
  selector: 'app-home',
  imports: [],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  // Every real entry point is a location code typed directly into the URL
  // (see app.routes.ts) — this page is public marketing only, so the only
  // interactive thing on it is a demo-request form that hands off to email
  // rather than posting anywhere.
  protected onSubmitDemoRequest(event: SubmitEvent): void {
    event.preventDefault();
    const data = new FormData(event.target as HTMLFormElement);
    const biz = ((data.get('biz') as string) ?? '').trim();
    const contactName = ((data.get('name') as string) ?? '').trim();
    const email = ((data.get('email') as string) ?? '').trim();
    const phone = ((data.get('phone') as string) ?? '').trim();
    const msg = ((data.get('msg') as string) ?? '').trim();

    const subject = `Demo request — ${biz || 'New business'}`;
    const bodyLines = [
      `Business: ${biz}`,
      `Contact: ${contactName}`,
      `Email: ${email}`,
      `Phone: ${phone || '(not provided)'}`,
      '',
      'What they want to solve:',
      msg || '(not provided)',
    ];
    const mailto =
      'mailto:neerajsaini1982@gmail.com' +
      `?subject=${encodeURIComponent(subject)}` +
      `&body=${encodeURIComponent(bodyLines.join('\n'))}`;
    window.location.href = mailto;
  }
}

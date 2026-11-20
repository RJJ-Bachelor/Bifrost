import {ComponentFixture, TestBed} from '@angular/core/testing';
import {WelcomeDialog} from './welcome-dialog';

describe('WelcomeDialog', () => {
  let fixture: ComponentFixture<WelcomeDialog>;

  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  beforeEach(async () => {
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [WelcomeDialog],
    }).compileComponents();

    fixture = TestBed.createComponent(WelcomeDialog);
    fixture.detectChanges();
  });

  it('opens on first visit and remembers dismissal', () => {
    expect(fixture.componentInstance.open()).toBe(true);

    const dismissBtn = fixture.nativeElement.querySelector('.form-actions .btn--primary');
    dismissBtn.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.open()).toBe(false);
    expect(localStorage.getItem('bifrost-welcome-seen')).toBe('true');
  });

  it('stays closed when already seen', () => {
    localStorage.setItem('bifrost-welcome-seen', 'true');

    const secondFixture = TestBed.createComponent(WelcomeDialog);
    secondFixture.detectChanges();

    expect(secondFixture.componentInstance.open()).toBe(false);
  });
});

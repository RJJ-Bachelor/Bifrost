import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { IdentityTransferCode } from '../../../../core/auth/auth.model';
import { AuthApi } from '../../../../core/auth/services/auth-api';
import { PushNotificationService } from '../../../../core/pwa/services/push-notification.service';
import { PwaService } from '../../../../core/pwa/services/pwa.service';
import { SettingsDialog } from './settings-dialog';

interface SettingsDialogInternals {
  step: () => 'choose' | 'share' | 'import';
  model: {
    (): { code: string };
    set: (value: { code: string }) => void;
  };
  importForm: () => { invalid: () => boolean };
  transferCode: () => IdentityTransferCode | null;
  copied: () => boolean;
  shareLoading: () => boolean;
  shareError: () => string | null;
  importLoading: () => boolean;
  importError: () => string | null;
  toggleNotifications: () => Promise<void>;
  share: () => void;
  startImport: () => void;
  goBack: () => void;
  close: () => void;
  generateCode: () => void;
  copyCode: () => Promise<void>;
  redeem: (event: Event) => void;
}

const GENERATED_CODE: IdentityTransferCode = {
  code: 'ABCD1234',
  expiresAtUtc: '2026-11-17T15:00:00Z',
};

describe('SettingsDialog', () => {
  let authApiMock: {
    createIdentityTransferCode: ReturnType<typeof vi.fn>;
    redeemIdentityTransferCode: ReturnType<typeof vi.fn>;
  };

  let pushMock: {
    isSupported: boolean;
    permission: ReturnType<typeof signal<NotificationPermission>>;
    isSubscribed: ReturnType<typeof signal<boolean>>;
    isLoading: ReturnType<typeof signal<boolean>>;
    error: ReturnType<typeof signal<string | null>>;
    enable: ReturnType<typeof vi.fn>;
    disable: ReturnType<typeof vi.fn>;
  };

  let pwaMock: {
    isIos: boolean;
    isPwa: boolean;
  };

  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  function setup(options: { open?: boolean; showTransfer?: boolean; isSupported?: boolean; isIos?: boolean; isPwa?: boolean } = {}): {
    fixture: ComponentFixture<SettingsDialog>;
    component: SettingsDialogInternals;
  } {
    authApiMock = {
      createIdentityTransferCode: vi.fn(() => of(GENERATED_CODE)),
      redeemIdentityTransferCode: vi.fn(() => of(void 0)),
    };

    pushMock = {
      isSupported: options.isSupported ?? true,
      permission: signal<NotificationPermission>('default'),
      isSubscribed: signal(false),
      isLoading: signal(false),
      error: signal<string | null>(null),
      enable: vi.fn(async () => true),
      disable: vi.fn(async () => undefined),
    };

    pwaMock = {
      isIos: options.isIos ?? false,
      isPwa: options.isPwa ?? false,
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthApi, useValue: authApiMock },
        { provide: PushNotificationService, useValue: pushMock },
        { provide: PwaService, useValue: pwaMock },
      ],
    });

    const fixture = TestBed.createComponent(SettingsDialog);
    fixture.componentRef.setInput('open', options.open ?? true);
    fixture.componentRef.setInput('showTransfer', options.showTransfer ?? false);
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as SettingsDialogInternals,
    };
  }

  describe('notifications section', () => {
    it('calls enable() when unsubscribed and disable() when subscribed', async () => {
      const { component } = setup();

      await component.toggleNotifications();
      expect(pushMock.enable).toHaveBeenCalledTimes(1);
      expect(pushMock.disable).not.toHaveBeenCalled();

      pushMock.isSubscribed.set(true);
      await component.toggleNotifications();
      expect(pushMock.disable).toHaveBeenCalledTimes(1);
    });

    it('renders blocked guidance when browser permission is denied', () => {
      const { fixture } = setup();
      pushMock.permission.set('denied');
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Notifikationer er blokeret i din browser');
      expect((fixture.nativeElement as HTMLElement).querySelector('.settings-notification-box')).toBeNull();
    });

    it('renders push error message and clears it on close', () => {
      const { fixture, component } = setup();
      pushMock.error.set('Kunne ikke aktivere notifikationer. Prøv igen.');
      fixture.detectChanges();

      const errorEl = (fixture.nativeElement as HTMLElement).querySelector('.form-error');
      expect(errorEl?.textContent?.trim()).toBe('Kunne ikke aktivere notifikationer. Prøv igen.');

      component.close();
      expect(pushMock.error()).toBeNull();
    });

    it('shows iOS PWA warning and guide instead of toggle on non-standalone iOS', () => {
      const { fixture } = setup({ isIos: true, isPwa: false });

      const el = fixture.nativeElement as HTMLElement;
      expect(el.textContent ?? '').toContain('Føj til hjemmeskærm');
      expect(el.querySelector('.settings-ios-warning')).not.toBeNull();
      expect(el.querySelector('.settings-notification-box')).toBeNull();
    });
  });

  describe('transfer visibility and state reset', () => {
    it('hides the transfer section by default and shows it when showTransfer is true', () => {
      const hidden = setup({ showTransfer: false });
      expect((hidden.fixture.nativeElement as HTMLElement).textContent).not.toContain('Overfør brugerdata');

      TestBed.resetTestingModule();

      const visible = setup({ showTransfer: true });
      expect((visible.fixture.nativeElement as HTMLElement).textContent).toContain('Overfør brugerdata');
    });

    it('resets step, generated code, and errors when reopened', () => {
      const { fixture, component } = setup({ showTransfer: true });
      component.share();
      expect(component.step()).toBe('share');
      expect(component.transferCode()).toEqual(GENERATED_CODE);

      fixture.componentRef.setInput('open', false);
      fixture.detectChanges();
      fixture.componentRef.setInput('open', true);
      fixture.detectChanges();

      expect(component.step()).toBe('choose');
      expect(component.transferCode()).toBeNull();
      expect(component.shareError()).toBeNull();
      expect(component.importError()).toBeNull();
    });
  });

  describe('transfer flows', () => {
    it('does not call the API again while code generation is in flight', () => {
      const { component } = setup({ showTransfer: true });
      const pending = new Subject<IdentityTransferCode>();
      authApiMock.createIdentityTransferCode.mockReturnValueOnce(pending.asObservable());

      component.share();
      component.share();

      expect(authApiMock.createIdentityTransferCode).toHaveBeenCalledTimes(1);
      pending.complete();
    });

    it('trims and uppercases the code on redeem and emits transferred', () => {
      const { fixture, component } = setup({ showTransfer: true });
      const transferredSpy = vi.fn();
      fixture.componentInstance.transferred.subscribe(transferredSpy);

      component.startImport();
      component.model.set({ code: '  abcd1234  ' });
      fixture.detectChanges();

      component.redeem(new Event('submit'));

      expect(authApiMock.redeemIdentityTransferCode).toHaveBeenCalledWith('ABCD1234');
      expect(transferredSpy).toHaveBeenCalledTimes(1);
    });

    it('shows rate-limit message on 429 and generic message on other errors', () => {
      const { fixture, component } = setup({ showTransfer: true });
      component.startImport();
      component.model.set({ code: 'ABCD1234' });
      fixture.detectChanges();

      authApiMock.redeemIdentityTransferCode.mockReturnValueOnce(
        throwError(() => new HttpErrorResponse({ status: 429 })),
      );
      component.redeem(new Event('submit'));
      expect(component.importError()).toBe('For mange forsøg. Vent et øjeblik og prøv igen.');

      authApiMock.redeemIdentityTransferCode.mockReturnValueOnce(
        throwError(() => new HttpErrorResponse({ status: 400 })),
      );
      component.redeem(new Event('submit'));
      expect(component.importError()).toBe('Koden er ugyldig eller udløbet. Prøv igen.');
    });
  });
});

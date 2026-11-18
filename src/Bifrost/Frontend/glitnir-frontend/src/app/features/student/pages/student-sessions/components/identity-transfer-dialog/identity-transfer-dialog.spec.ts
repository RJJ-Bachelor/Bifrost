import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { IdentityTransferCode } from '../../../../../../core/auth/auth.model';
import { AuthApi } from '../../../../../../core/auth/services/auth-api';
import { IdentityTransferDialog } from './identity-transfer-dialog';

interface IdentityTransferDialogInternals {
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

describe('IdentityTransferDialog', () => {
  let authApiMock: {
    createIdentityTransferCode: ReturnType<typeof vi.fn>;
    redeemIdentityTransferCode: ReturnType<typeof vi.fn>;
  };

  // jsdom does not implement the native <dialog> methods used by Dialog.
  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  function setup(initialOpen = true): {
    fixture: ComponentFixture<IdentityTransferDialog>;
    component: IdentityTransferDialogInternals;
  } {
    authApiMock = {
      createIdentityTransferCode: vi.fn(() => of(GENERATED_CODE)),
      redeemIdentityTransferCode: vi.fn(() => of(void 0)),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: AuthApi, useValue: authApiMock }],
    });

    const fixture = TestBed.createComponent(IdentityTransferDialog);
    fixture.componentRef.setInput('open', initialOpen);
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as IdentityTransferDialogInternals,
    };
  }

  describe('initial state', () => {
    it('starts on the choose view without calling the API', () => {
      const { component } = setup();

      expect(component.step()).toBe('choose');
      expect(authApiMock.createIdentityTransferCode).not.toHaveBeenCalled();
      expect(authApiMock.redeemIdentityTransferCode).not.toHaveBeenCalled();
    });

    it('resets to the choose view when the dialog is reopened', () => {
      const { fixture, component } = setup();
      component.startImport();
      expect(component.step()).toBe('import');

      fixture.componentRef.setInput('open', false);
      fixture.detectChanges();
      fixture.componentRef.setInput('open', true);
      fixture.detectChanges();

      expect(component.step()).toBe('choose');
    });
  });

  describe('share flow', () => {
    it('creates a transfer code and stores the result', () => {
      const { component } = setup();

      component.share();

      expect(component.step()).toBe('share');
      expect(authApiMock.createIdentityTransferCode).toHaveBeenCalledTimes(1);
      expect(component.transferCode()).toEqual(GENERATED_CODE);
      expect(component.shareLoading()).toBe(false);
    });

    it('shows an error message and clears the code when the API fails', () => {
      const { component } = setup();
      authApiMock.createIdentityTransferCode.mockReturnValueOnce(
        throwError(() => new Error('boom')),
      );

      component.share();

      expect(component.transferCode()).toBeNull();
      expect(component.shareError()).toBe(
        'Kunne ikke oprette en overførselskode. Prøv igen.',
      );
    });

    it('does not call the API again while a request is in flight', () => {
      const { component } = setup();
      const pending = new Subject<IdentityTransferCode>();
      authApiMock.createIdentityTransferCode.mockReturnValueOnce(
        pending.asObservable(),
      );

      component.share();
      component.share();

      expect(authApiMock.createIdentityTransferCode).toHaveBeenCalledTimes(1);
      pending.complete();
    });

    it('copies the generated code to the clipboard', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined);
      Object.assign(navigator, { clipboard: { writeText } });

      const { component } = setup();
      component.share();

      await component.copyCode();

      expect(writeText).toHaveBeenCalledWith('ABCD1234');
      expect(component.copied()).toBe(true);
    });

    it('returns to the choose view when goBack is called', () => {
      const { component } = setup();
      component.share();

      component.goBack();

      expect(component.step()).toBe('choose');
      expect(component.shareError()).toBeNull();
    });
  });

  describe('import flow', () => {
    it('is invalid when the code is empty or only whitespace', () => {
      const { fixture, component } = setup();
      component.startImport();
      fixture.detectChanges();

      expect(component.importForm().invalid()).toBe(true);

      component.model.set({ code: '   ' });
      fixture.detectChanges();
      expect(component.importForm().invalid()).toBe(true);
    });

    it('is valid when a non-empty code is entered', () => {
      const { fixture, component } = setup();
      component.startImport();
      component.model.set({ code: '  abcd1234  ' });
      fixture.detectChanges();

      expect(component.importForm().invalid()).toBe(false);
    });

    it('trims and uppercases the code before redeeming and emits transferred', () => {
      const { fixture, component } = setup();
      component.startImport();
      component.model.set({ code: '  ab12cd34 ' });
      fixture.detectChanges();

      const transferredSpy = vi.fn();
      fixture.componentInstance.transferred.subscribe(transferredSpy);

      component.redeem(new Event('submit'));

      expect(authApiMock.redeemIdentityTransferCode).toHaveBeenCalledWith(
        'AB12CD34',
      );
      expect(transferredSpy).toHaveBeenCalledTimes(1);
      expect(component.importLoading()).toBe(false);
    });

    it('does nothing when the form is invalid', () => {
      const { component } = setup();
      component.startImport();

      component.redeem(new Event('submit'));

      expect(authApiMock.redeemIdentityTransferCode).not.toHaveBeenCalled();
    });

    it('shows an error message when the API fails', () => {
      const { fixture, component } = setup();
      authApiMock.redeemIdentityTransferCode.mockReturnValueOnce(
        throwError(() => new Error('boom')),
      );
      component.startImport();
      component.model.set({ code: 'ABCD1234' });
      fixture.detectChanges();

      const transferredSpy = vi.fn();
      fixture.componentInstance.transferred.subscribe(transferredSpy);

      component.redeem(new Event('submit'));

      expect(component.importError()).toBe(
        'Koden er ugyldig eller udløbet. Prøv igen.',
      );
      expect(transferredSpy).not.toHaveBeenCalled();
    });

    it('shows a distinct message when the request is rate limited', () => {
      const { fixture, component } = setup();
      authApiMock.redeemIdentityTransferCode.mockReturnValueOnce(
        throwError(() => new HttpErrorResponse({ status: 429 })),
      );
      component.startImport();
      component.model.set({ code: 'ABCD1234' });
      fixture.detectChanges();

      component.redeem(new Event('submit'));

      expect(component.importError()).toBe(
        'For mange forsøg. Vent et øjeblik og prøv igen.',
      );
    });
  });

  describe('close', () => {
    it('emits closed', () => {
      const { fixture, component } = setup();

      const closedSpy = vi.fn();
      fixture.componentInstance.closed.subscribe(closedSpy);

      component.close();

      expect(closedSpy).toHaveBeenCalledTimes(1);
    });
  });
});

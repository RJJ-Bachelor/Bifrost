import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SessionApi } from '../../../../../session';
import { SessionResetDialog } from './session-reset-dialog';

interface SessionResetDialogInternals {
  clearing: () => boolean;
  error: () => string | null;
  onCancel: () => void;
  onConfirm: () => void;
}

describe('SessionResetDialog', () => {
  let sessionApiMock: {
    clearSession: ReturnType<typeof vi.fn>;
  };

  // jsdom does not implement the native <dialog> methods used by Dialog.
  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  function setup(
    open: boolean,
    generation: number | undefined = 1,
  ): {
    fixture: ComponentFixture<SessionResetDialog>;
    component: SessionResetDialogInternals;
  } {
    sessionApiMock = {
      clearSession: vi.fn(() =>
        of({ sessionId: 'session-1', currentGeneration: 2, lastClearedAtUtc: '2026-10-25T12:00:00Z' }),
      ),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: SessionApi, useValue: sessionApiMock }],
    });

    const fixture = TestBed.createComponent(SessionResetDialog);
    fixture.componentRef.setInput('open', open);
    fixture.componentRef.setInput('sessionId', 'session-1');
    fixture.componentRef.setInput('generation', generation);
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as SessionResetDialogInternals,
    };
  }

  describe('error', () => {
    it('starts as null', () => {
      const { component } = setup(true);
      expect(component.error()).toBeNull();
    });

    it('resets to null when the dialog is reopened', () => {
      const { fixture, component } = setup(true);
      sessionApiMock.clearSession.mockReturnValue(
        throwError(() => new Error('boom')),
      );
      component.onConfirm();
      expect(component.error()).toBe('Kunne ikke rydde sessionsdata. Prøv igen.');

      fixture.componentRef.setInput('open', false);
      fixture.detectChanges();
      fixture.componentRef.setInput('open', true);
      fixture.detectChanges();

      expect(component.error()).toBeNull();
    });
  });

  describe('onCancel', () => {
    it('emits cancelled', () => {
      const { fixture, component } = setup(true);

      const cancelledSpy = vi.fn();
      fixture.componentInstance.cancelled.subscribe(cancelledSpy);

      component.onCancel();

      expect(cancelledSpy).toHaveBeenCalled();
    });
  });

  describe('onConfirm', () => {
    it('does nothing when generation is undefined', () => {
      const { component } = setup(true, undefined);

      component.onConfirm();

      expect(sessionApiMock.clearSession).not.toHaveBeenCalled();
    });

    it('clears the session and emits cleared', () => {
      const { fixture, component } = setup(true);

      const clearedSpy = vi.fn();
      fixture.componentInstance.cleared.subscribe(clearedSpy);

      component.onConfirm();

      expect(sessionApiMock.clearSession).toHaveBeenCalledWith('session-1', 1);
      expect(clearedSpy).toHaveBeenCalled();
      expect(component.clearing()).toBe(false);
    });

    it('sets the error and does not emit cleared when the API fails', () => {
      const { fixture, component } = setup(true);
      sessionApiMock.clearSession.mockReturnValue(
        throwError(() => new Error('boom')),
      );

      const clearedSpy = vi.fn();
      fixture.componentInstance.cleared.subscribe(clearedSpy);

      component.onConfirm();

      expect(component.error()).toBe('Kunne ikke rydde sessionsdata. Prøv igen.');
      expect(clearedSpy).not.toHaveBeenCalled();
      expect(component.clearing()).toBe(false);
    });
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SessionApi } from '../../../../../session/services/session-api';
import { SessionDeleteDialog } from './session-delete-dialog';

interface SessionDeleteDialogInternals {
  deleting: () => boolean;
  error: () => string | null;
  onCancel: () => void;
  onConfirm: () => void;
}

describe('SessionDeleteDialog', () => {
  let sessionApiMock: {
    deleteSession: ReturnType<typeof vi.fn>;
  };

  // jsdom does not implement the native <dialog> methods used by Dialog.
  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  function setup(open: boolean): {
    fixture: ComponentFixture<SessionDeleteDialog>;
    component: SessionDeleteDialogInternals;
  } {
    sessionApiMock = {
      deleteSession: vi.fn(() => of(undefined)),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: SessionApi, useValue: sessionApiMock }],
    });

    const fixture = TestBed.createComponent(SessionDeleteDialog);
    fixture.componentRef.setInput('open', open);
    fixture.componentRef.setInput('sessionId', 'session-1');
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as SessionDeleteDialogInternals,
    };
  }

  describe('error', () => {
    it('starts as null', () => {
      const { component } = setup(true);
      expect(component.error()).toBeNull();
    });

    it('resets to null when the dialog is reopened', () => {
      const { fixture, component } = setup(true);
      sessionApiMock.deleteSession.mockReturnValue(
        throwError(() => new Error('boom')),
      );
      component.onConfirm();
      expect(component.error()).toBe('Kunne ikke slette sessionen. Prøv igen.');

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
    it('deletes the session and emits deleted', () => {
      const { fixture, component } = setup(true);

      const deletedSpy = vi.fn();
      fixture.componentInstance.deleted.subscribe(deletedSpy);

      component.onConfirm();

      expect(sessionApiMock.deleteSession).toHaveBeenCalledWith('session-1');
      expect(deletedSpy).toHaveBeenCalled();
    });

    it('sets the error and does not emit deleted when the API fails', () => {
      const { fixture, component } = setup(true);
      sessionApiMock.deleteSession.mockReturnValue(
        throwError(() => new Error('boom')),
      );

      const deletedSpy = vi.fn();
      fixture.componentInstance.deleted.subscribe(deletedSpy);

      component.onConfirm();

      expect(component.error()).toBe('Kunne ikke slette sessionen. Prøv igen.');
      expect(deletedSpy).not.toHaveBeenCalled();
    });
  });
});

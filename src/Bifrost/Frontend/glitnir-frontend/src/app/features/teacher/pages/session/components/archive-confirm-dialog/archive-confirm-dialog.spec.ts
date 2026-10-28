import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SessionApi } from '../../../../../session';
import { ArchiveConfirmDialog } from './archive-confirm-dialog';

interface ArchiveConfirmDialogInternals {
  title: {
    (): string;
    set: (value: string) => void;
  };
  archiving: () => boolean;
  error: () => string | null;
  onTitleInput: (event: Event) => void;
  onCancel: () => void;
  onConfirm: () => void;
}

describe('ArchiveConfirmDialog', () => {
  let sessionApiMock: {
    archiveSession: ReturnType<typeof vi.fn>;
  };

  // jsdom does not implement the native <dialog> methods used by Dialog.
  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  function setup(open: boolean, generation: number | null = 8): {
    fixture: ComponentFixture<ArchiveConfirmDialog>;
    component: ArchiveConfirmDialogInternals;
  } {
    sessionApiMock = {
      archiveSession: vi.fn(() => of({})),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: SessionApi, useValue: sessionApiMock }],
    });

    const fixture = TestBed.createComponent(ArchiveConfirmDialog);
    fixture.componentRef.setInput('open', open);
    fixture.componentRef.setInput('sessionId', 'session-1');
    fixture.componentRef.setInput('generation', generation ?? undefined);
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as ArchiveConfirmDialogInternals,
    };
  }

  describe('title', () => {
    it('starts empty', () => {
      const { component } = setup(true);
      expect(component.title()).toBe('');
    });

    it('resets to empty when the dialog is reopened', () => {
      const { fixture, component } = setup(true);

      component.title.set('Lek 8');
      expect(component.title()).toBe('Lek 8');

      fixture.componentRef.setInput('open', false);
      fixture.detectChanges();
      fixture.componentRef.setInput('open', true);
      fixture.detectChanges();

      expect(component.title()).toBe('');
    });
  });

  describe('error', () => {
    it('starts as null', () => {
      const { component } = setup(true);
      expect(component.error()).toBeNull();
    });

    it('resets to null when the dialog is reopened', () => {
      const { fixture, component } = setup(true);
      sessionApiMock.archiveSession.mockReturnValue(
        throwError(() => new Error('boom')),
      );
      component.title.set('Lek 8');
      component.onConfirm();
      expect(component.error()).toBe('Kunne ikke arkivere. Prøv igen.');

      fixture.componentRef.setInput('open', false);
      fixture.detectChanges();
      fixture.componentRef.setInput('open', true);
      fixture.detectChanges();

      expect(component.error()).toBeNull();
    });
  });

  describe('onTitleInput', () => {
    it('updates title from the input event target value', () => {
      const { component } = setup(true);
      const input = document.createElement('input');
      input.value = 'Lek 9 - Indekser';

      component.onTitleInput({ target: input } as unknown as Event);

      expect(component.title()).toBe('Lek 9 - Indekser');
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
    it('archives the current generation with the given title', () => {
      const { fixture, component } = setup(true, 8);
      component.title.set('Lek 8');

      const archivedSpy = vi.fn();
      fixture.componentInstance.archived.subscribe(archivedSpy);

      component.onConfirm();

      expect(sessionApiMock.archiveSession).toHaveBeenCalledWith(
        'session-1',
        8,
        'Lek 8',
      );
      expect(archivedSpy).toHaveBeenCalled();
    });

    it('does nothing when the title is empty', () => {
      const { fixture, component } = setup(true);
      component.title.set('   ');

      const archivedSpy = vi.fn();
      fixture.componentInstance.archived.subscribe(archivedSpy);

      component.onConfirm();

      expect(sessionApiMock.archiveSession).not.toHaveBeenCalled();
      expect(archivedSpy).not.toHaveBeenCalled();
    });

    it('does nothing when generation is undefined', () => {
      const { fixture, component } = setup(true, null);
      component.title.set('Lek 8');

      const archivedSpy = vi.fn();
      fixture.componentInstance.archived.subscribe(archivedSpy);

      component.onConfirm();

      expect(sessionApiMock.archiveSession).not.toHaveBeenCalled();
      expect(archivedSpy).not.toHaveBeenCalled();
    });

    it('sets the error and does not emit archived when the API fails', () => {
      const { fixture, component } = setup(true);
      sessionApiMock.archiveSession.mockReturnValue(
        throwError(() => new Error('boom')),
      );
      component.title.set('Lek 8');

      const archivedSpy = vi.fn();
      fixture.componentInstance.archived.subscribe(archivedSpy);

      component.onConfirm();

      expect(component.error()).toBe('Kunne ikke arkivere. Prøv igen.');
      expect(archivedSpy).not.toHaveBeenCalled();
    });
  });
});

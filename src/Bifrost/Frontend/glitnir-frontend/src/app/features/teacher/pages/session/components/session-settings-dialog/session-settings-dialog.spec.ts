import { ComponentFixture, TestBed } from '@angular/core/testing';
import { type Session } from '../../../../../session';
import { SessionSettingsDialog } from './session-settings-dialog';

interface SessionSettingsDialogInternals {
  studentCode: () => string;
  teacherCode: () => string;
  renameModel: {
    (): string;
    set: (value: string) => void;
  };
  renaming: () => boolean;
  renameError: () => string | null;
  onClose: () => void;
  onRenameInput: (event: Event) => void;
  onSubmitRename: (event: Event) => void;
  onRequestDelete: () => void;
}

function makeSession(overrides: Partial<Session> = {}): Session {
  return {
    id: 'session-1',
    title: 'E26SWBAD',
    currentGeneration: 1,
    lastClearedAtUtc: null,
    code: 'STUD-123',
    teacherCode: 'TEACH-456',
    ...overrides,
  } as Session;
}

describe('SessionSettingsDialog', () => {
  // jsdom does not implement the native <dialog> methods used by Dialog.
  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  function setup(
    open: boolean,
    session: Session | null = makeSession(),
  ): {
    fixture: ComponentFixture<SessionSettingsDialog>;
    component: SessionSettingsDialogInternals;
  } {
    TestBed.configureTestingModule({});

    const fixture = TestBed.createComponent(SessionSettingsDialog);
    fixture.componentRef.setInput('open', open);
    fixture.componentRef.setInput('session', session ?? undefined);
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as SessionSettingsDialogInternals,
    };
  }

  describe('studentCode', () => {
    it('returns the session code', () => {
      const { component } = setup(true, makeSession({ code: 'STUD-123' }));
      expect(component.studentCode()).toBe('STUD-123');
    });

    it('returns an em dash when the session is undefined', () => {
      const { component } = setup(true, null);
      expect(component.studentCode()).toBe('—');
    });
  });

  describe('teacherCode', () => {
    it('returns the session teacherCode', () => {
      const { component } = setup(true, makeSession({ teacherCode: 'TEACH-456' }));
      expect(component.teacherCode()).toBe('TEACH-456');
    });

    it('returns an em dash when the session is undefined', () => {
      const { component } = setup(true, null);
      expect(component.teacherCode()).toBe('—');
    });
  });

  describe('renameModel', () => {
    it('is prefilled with the session title when opened', () => {
      const { component } = setup(true, makeSession({ title: 'E26SWBAD' }));
      expect(component.renameModel()).toBe('E26SWBAD');
    });

    it('is empty when closed', () => {
      const { component } = setup(false);
      expect(component.renameModel()).toBe('');
    });

    it('resets to the session title when reopened', () => {
      const { fixture, component } = setup(true, makeSession({ title: 'E26SWBAD' }));

      component.renameModel.set('Draft title');
      expect(component.renameModel()).toBe('Draft title');

      fixture.componentRef.setInput('open', false);
      fixture.detectChanges();
      fixture.componentRef.setInput('open', true);
      fixture.detectChanges();

      expect(component.renameModel()).toBe('E26SWBAD');
    });

    it('does not reset when the session changes while open', () => {
      const { fixture, component } = setup(true, makeSession({ title: 'E26SWBAD' }));

      component.renameModel.set('Draft title');

      fixture.componentRef.setInput('session', makeSession({ title: 'F27SWBAD' }));
      fixture.detectChanges();

      expect(component.renameModel()).toBe('Draft title');
    });
  });

  describe('onClose', () => {
    it('emits closed', () => {
      const { fixture, component } = setup(true);

      const closedSpy = vi.fn();
      fixture.componentInstance.closed.subscribe(closedSpy);

      component.onClose();

      expect(closedSpy).toHaveBeenCalled();
    });
  });

  describe('onRenameInput', () => {
    it('updates renameModel from the input event target value', () => {
      const { component } = setup(true);
      const input = document.createElement('input');
      input.value = 'F27SWBAD';

      component.onRenameInput({ target: input } as unknown as Event);

      expect(component.renameModel()).toBe('F27SWBAD');
    });
  });

  describe('onSubmitRename', () => {
    it('does nothing when the title is empty', () => {
      const { component } = setup(true);
      component.renameModel.set('   ');

      component.onSubmitRename(new Event('submit'));

      expect(component.renaming()).toBe(false);
    });

    it('sets renaming while the request is in flight', () => {
      const { component } = setup(true);
      component.renameModel.set('F27SWBAD');

      component.onSubmitRename(new Event('submit'));

      expect(component.renaming()).toBe(true);
    });
  });

  describe('onRequestDelete', () => {
    it('emits deleteRequested', () => {
      const { fixture, component } = setup(true);

      const deleteSpy = vi.fn();
      fixture.componentInstance.deleteRequested.subscribe(deleteSpy);

      component.onRequestDelete();

      expect(deleteSpy).toHaveBeenCalled();
    });
  });
});

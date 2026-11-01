import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { FaqApi } from '../../services/faq-api';
import { FaqDialogState, FaqFormDialog } from './faq-form-dialog';

interface FaqFormDialogInternals {
  model: {
    (): { question: string; answer: string };
    set: (value: { question: string; answer: string }) => void;
  };
  faqForm: () => { invalid: () => boolean };
  dialogOpen: () => boolean;
  error: () => string | null;
  submitting: () => boolean;
  onSubmit: (event: Event) => void;
  onCancel: () => void;
}

const CLOSED_STATE: FaqDialogState = { mode: 'closed' };
const CREATE_STATE: FaqDialogState = { mode: 'create' };

describe('FaqFormDialog', () => {
  let faqApiMock: {
    createFaq: ReturnType<typeof vi.fn>;
  };

  // jsdom does not implement the native <dialog> methods used by Dialog.
  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  function setup(initialState: FaqDialogState): {
    fixture: ComponentFixture<FaqFormDialog>;
    component: FaqFormDialogInternals;
  } {
    faqApiMock = {
      createFaq: vi.fn(() => of({ id: 'faq-1', question: 'Q', answer: 'A' })),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: FaqApi, useValue: faqApiMock }],
    });

    const fixture = TestBed.createComponent(FaqFormDialog);
    fixture.componentRef.setInput('state', initialState);
    fixture.componentRef.setInput('sessionId', 'session-1');
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as FaqFormDialogInternals,
    };
  }

  describe('dialogOpen', () => {
    it('is false when state is closed', () => {
      const { component } = setup(CLOSED_STATE);
      expect(component.dialogOpen()).toBe(false);
    });

    it('is true when state is create', () => {
      const { component } = setup(CREATE_STATE);
      expect(component.dialogOpen()).toBe(true);
    });
  });

  describe('model', () => {
    it('starts empty in create mode', () => {
      const { component } = setup(CREATE_STATE);
      expect(component.model()).toEqual({ question: '', answer: '' });
    });

    it('resets to empty when state changes from closed to create', () => {
      const { fixture, component } = setup(CLOSED_STATE);

      fixture.componentRef.setInput('state', CREATE_STATE);
      fixture.detectChanges();

      expect(component.model()).toEqual({ question: '', answer: '' });
    });
  });

  describe('validation', () => {
    it('is invalid when both fields are empty', () => {
      const { component } = setup(CREATE_STATE);
      expect(component.faqForm().invalid()).toBe(true);
    });

    it('is invalid when the question is empty', () => {
      const { fixture, component } = setup(CREATE_STATE);
      component.model.set({ question: '', answer: 'A' });
      fixture.detectChanges();
      expect(component.faqForm().invalid()).toBe(true);
    });

    it('is invalid when the answer is empty', () => {
      const { fixture, component } = setup(CREATE_STATE);
      component.model.set({ question: 'Q', answer: '' });
      fixture.detectChanges();
      expect(component.faqForm().invalid()).toBe(true);
    });

    it('is valid when both fields have content', () => {
      const { fixture, component } = setup(CREATE_STATE);
      component.model.set({ question: 'Q', answer: 'A' });
      fixture.detectChanges();
      expect(component.faqForm().invalid()).toBe(false);
    });
  });

  describe('onSubmit', () => {
    it('creates a new FAQ and emits saved', () => {
      const { fixture, component } = setup(CREATE_STATE);
      component.model.set({ question: 'New Q', answer: 'New A' });
      fixture.detectChanges();

      const savedSpy = vi.fn();
      fixture.componentInstance.saved.subscribe(savedSpy);

      component.onSubmit(new Event('submit'));

      expect(faqApiMock.createFaq).toHaveBeenCalledWith(
        'session-1',
        'New Q',
        'New A',
      );
      expect(savedSpy).toHaveBeenCalledWith({
        question: 'New Q',
        answer: 'New A',
      });
    });

    it('does nothing when the form is invalid', () => {
      const { fixture, component } = setup(CREATE_STATE);

      const savedSpy = vi.fn();
      fixture.componentInstance.saved.subscribe(savedSpy);

      component.onSubmit(new Event('submit'));

      expect(faqApiMock.createFaq).not.toHaveBeenCalled();
      expect(savedSpy).not.toHaveBeenCalled();
    });

    it('sets the error message and does not emit saved when the API fails', () => {
      const { fixture, component } = setup(CREATE_STATE);
      faqApiMock.createFaq.mockReturnValue(throwError(() => new Error('boom')));
      component.model.set({ question: 'Q', answer: 'A' });
      fixture.detectChanges();

      const savedSpy = vi.fn();
      fixture.componentInstance.saved.subscribe(savedSpy);

      component.onSubmit(new Event('submit'));

      expect(component.error()).toBe('Kunne ikke gemme FAQ. Prøv igen.');
      expect(savedSpy).not.toHaveBeenCalled();
    });
  });

  describe('onCancel', () => {
    it('emits cancelled', () => {
      const { fixture, component } = setup(CREATE_STATE);

      const cancelledSpy = vi.fn();
      fixture.componentInstance.cancelled.subscribe(cancelledSpy);

      component.onCancel();

      expect(cancelledSpy).toHaveBeenCalled();
    });
  });
});

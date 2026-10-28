import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { FaqItem } from '../../faq-item.model';
import { FaqApi } from '../../services/faq-api';
import { FaqDeleteDialog } from './faq-delete-dialog';

interface FaqDeleteDialogInternals {
  dialogOpen: () => boolean;
  deleting: () => boolean;
  error: () => string | null;
  onCancel: () => void;
  onConfirm: () => void;
}

function makeItem(overrides: Partial<FaqItem> = {}): FaqItem {
  return {
    id: 'faq-1',
    question: 'Question text',
    answer: 'Answer text',
    ...overrides,
  };
}

describe('FaqDeleteDialog', () => {
  let faqApiMock: {
    deleteFaq: ReturnType<typeof vi.fn>;
  };

  // jsdom does not implement the native <dialog> methods used by Dialog.
  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
  });

  function setup(item: FaqItem | null): {
    fixture: ComponentFixture<FaqDeleteDialog>;
    component: FaqDeleteDialogInternals;
  } {
    faqApiMock = {
      deleteFaq: vi.fn(() => of(undefined)),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: FaqApi, useValue: faqApiMock }],
    });

    const fixture = TestBed.createComponent(FaqDeleteDialog);
    fixture.componentRef.setInput('item', item);
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as FaqDeleteDialogInternals,
    };
  }

  describe('dialogOpen', () => {
    it('is false when item is null', () => {
      const { component } = setup(null);
      expect(component.dialogOpen()).toBe(false);
    });

    it('is true when item is set', () => {
      const { component } = setup(makeItem());
      expect(component.dialogOpen()).toBe(true);
    });
  });

  describe('error', () => {
    it('starts as null', () => {
      const { component } = setup(makeItem());
      expect(component.error()).toBeNull();
    });

    it('resets to null when the target item changes', () => {
      const { fixture, component } = setup(makeItem({ id: 'faq-1' }));
      faqApiMock.deleteFaq.mockReturnValue(throwError(() => new Error('boom')));
      component.onConfirm();
      expect(component.error()).toBe('Kunne ikke slette FAQ. Prøv igen.');

      fixture.componentRef.setInput('item', makeItem({ id: 'faq-2' }));
      fixture.detectChanges();

      expect(component.error()).toBeNull();
    });
  });

  describe('onCancel', () => {
    it('emits cancelled', () => {
      const { fixture, component } = setup(makeItem());

      const cancelledSpy = vi.fn();
      fixture.componentInstance.cancelled.subscribe(cancelledSpy);

      component.onCancel();

      expect(cancelledSpy).toHaveBeenCalled();
    });
  });

  describe('onConfirm', () => {
    it('deletes the FAQ and emits deleted', () => {
      const { fixture, component } = setup(makeItem({ id: 'faq-42' }));

      const deletedSpy = vi.fn();
      fixture.componentInstance.deleted.subscribe(deletedSpy);

      component.onConfirm();

      expect(faqApiMock.deleteFaq).toHaveBeenCalledWith('faq-42');
      expect(deletedSpy).toHaveBeenCalled();
    });

    it('sets the error and does not emit deleted when the API fails', () => {
      const { fixture, component } = setup(makeItem());
      faqApiMock.deleteFaq.mockReturnValue(throwError(() => new Error('boom')));

      const deletedSpy = vi.fn();
      fixture.componentInstance.deleted.subscribe(deletedSpy);

      component.onConfirm();

      expect(component.error()).toBe('Kunne ikke slette FAQ. Prøv igen.');
      expect(deletedSpy).not.toHaveBeenCalled();
    });
  });
});

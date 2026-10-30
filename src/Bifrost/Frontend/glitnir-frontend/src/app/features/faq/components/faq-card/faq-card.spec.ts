import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FaqAnswerSubmission, FaqCard } from './faq-card';

interface ComponentAccess {
  editingQuestion: () => boolean;
  editedQuestion: () => string;
  answerDraft: () => string;
  startEditingQuestion: () => void;
  saveQuestion: () => void;
  cancelEditing: () => void;
  onQuestionInput: (event: Event) => void;
  onQuestionKeydown: (event: KeyboardEvent) => void;
  onAnswerInput: (event: Event) => void;
  submitAnswer: () => void;
}

interface SetupResult {
  fixture: ComponentFixture<FaqCard>;
  component: ComponentAccess;
  submissions: FaqAnswerSubmission[];
}

function setup(inputs: {
  question: string;
  busy?: boolean;
  editable?: boolean;
}): SetupResult {
  TestBed.configureTestingModule({});

  const fixture = TestBed.createComponent(FaqCard);
  fixture.componentRef.setInput('question', inputs.question);
  if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
  if (inputs.editable !== undefined) fixture.componentRef.setInput('editable', inputs.editable);
  fixture.detectChanges();

  const submissions: FaqAnswerSubmission[] = [];
  fixture.componentInstance.answered.subscribe((s) => submissions.push(s));

  return {
    fixture,
    component: fixture.componentInstance as unknown as ComponentAccess,
    submissions,
  };
}

function textareaInput(value: string): Event {
  return { target: { value } } as unknown as Event;
}

function keydownEvent(key: string): KeyboardEvent {
  return {
    key,
    preventDefault: vi.fn(),
    target: { blur: vi.fn() },
  } as unknown as KeyboardEvent;
}

describe('FaqCard', () => {
  describe('submitAnswer', () => {
    it('does not emit when the answer draft is empty', () => {
      const { component, submissions } = setup({ question: 'Question' });
      component.submitAnswer();
      expect(submissions).toHaveLength(0);
    });

    it('does not emit when the answer draft is only whitespace', () => {
      const { component, submissions } = setup({ question: 'Question' });
      component.onAnswerInput(textareaInput('   \n\t  '));
      component.submitAnswer();
      expect(submissions).toHaveLength(0);
    });

    it('does not emit while busy', () => {
      const { component, submissions } = setup({ question: 'Question', busy: true });
      component.onAnswerInput(textareaInput('An answer'));
      component.submitAnswer();
      expect(submissions).toHaveLength(0);
    });

    it('emits the trimmed answer without a question when the question is unchanged', () => {
      const { component, submissions } = setup({ question: 'Original question' });
      component.onAnswerInput(textareaInput('  An answer  '));
      component.submitAnswer();
      expect(submissions).toEqual([{ text: 'An answer', question: undefined }]);
    });

    it('emits the edited question when the user has changed it', () => {
      const { component, submissions } = setup({ question: 'Original question' });
      component.onAnswerInput(textareaInput('An answer'));
      component.onQuestionInput(textareaInput('Revised question'));
      component.submitAnswer();
      expect(submissions).toEqual([{ text: 'An answer', question: 'Revised question' }]);
    });

    it('trims the edited question before comparing and emitting', () => {
      const { component, submissions } = setup({ question: 'Original question' });
      component.onAnswerInput(textareaInput('An answer'));
      component.onQuestionInput(textareaInput('  Revised question  '));
      component.submitAnswer();
      expect(submissions).toEqual([{ text: 'An answer', question: 'Revised question' }]);
    });

    it('omits the question when the edit only adds whitespace', () => {
      const { component, submissions } = setup({ question: 'Original question' });
      component.onAnswerInput(textareaInput('An answer'));
      component.onQuestionInput(textareaInput('   Original question   '));
      component.submitAnswer();
      expect(submissions).toEqual([{ text: 'An answer', question: undefined }]);
    });

    it('omits the question when the edit is cleared to whitespace only', () => {
      const { component, submissions } = setup({ question: 'Original question' });
      component.onAnswerInput(textareaInput('An answer'));
      component.onQuestionInput(textareaInput('    '));
      component.submitAnswer();
      expect(submissions).toEqual([{ text: 'An answer', question: undefined }]);
    });
  });

  describe('saveQuestion', () => {
    it('reverts to the original question when the edit is empty', () => {
      const { component } = setup({ question: 'Original question' });
      component.startEditingQuestion();
      component.onQuestionInput(textareaInput('   '));
      component.saveQuestion();
      expect(component.editedQuestion()).toBe('Original question');
      expect(component.editingQuestion()).toBe(false);
    });

    it('keeps the edited question and exits edit mode', () => {
      const { component } = setup({ question: 'Original question' });
      component.startEditingQuestion();
      component.onQuestionInput(textareaInput('Revised question'));
      component.saveQuestion();
      expect(component.editedQuestion()).toBe('Revised question');
      expect(component.editingQuestion()).toBe(false);
    });
  });

  describe('cancelEditing', () => {
    it('reverts the edited question and exits edit mode', () => {
      const { component } = setup({ question: 'Original question' });
      component.startEditingQuestion();
      component.onQuestionInput(textareaInput('Revised question'));
      component.cancelEditing();
      expect(component.editedQuestion()).toBe('Original question');
      expect(component.editingQuestion()).toBe(false);
    });
  });

  describe('onQuestionKeydown', () => {
    it('cancels editing when Escape is pressed', () => {
      const { component } = setup({ question: 'Original question' });
      component.startEditingQuestion();
      component.onQuestionInput(textareaInput('Revised question'));
      component.onQuestionKeydown(keydownEvent('Escape'));
      expect(component.editedQuestion()).toBe('Original question');
      expect(component.editingQuestion()).toBe(false);
    });

    it('does not cancel editing on other keys', () => {
      const { component } = setup({ question: 'Original question' });
      component.startEditingQuestion();
      component.onQuestionInput(textareaInput('Revised question'));
      component.onQuestionKeydown(keydownEvent('a'));
      expect(component.editedQuestion()).toBe('Revised question');
      expect(component.editingQuestion()).toBe(true);
    });
  });
});

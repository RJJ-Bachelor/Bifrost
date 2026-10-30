import { TestBed } from '@angular/core/testing';
import { AuthorRole, HelpRequestMessage } from '../../help-request.model';
import { Chat } from './chat';

interface ComponentAccess {
  visibleMessages: () => HelpRequestMessage[];
  draft: () => string;
  setDraft: (value: string) => void;
  onSubmit: (event: Event) => void;
  submitDraft: () => void;
}

interface SetupResult {
  component: ComponentAccess;
  sent: string[];
}

function makeMessage(overrides: Partial<HelpRequestMessage> = {}): HelpRequestMessage {
  return {
    id: 'message-1',
    authorRole: 'student',
    messageType: 'studentQuestion',
    text: 'Hej',
    createdAt: '2026-01-01T10:00:00Z',
    sequenceNumber: 1,
    ...overrides,
  };
}

function setup(inputs: {
  messages?: HelpRequestMessage[];
  viewerRole?: AuthorRole;
  sending?: boolean;
} = {}): SetupResult {
  TestBed.configureTestingModule({});

  const fixture = TestBed.createComponent(Chat);
  fixture.componentRef.setInput('messages', inputs.messages ?? []);
  fixture.componentRef.setInput('viewerRole', inputs.viewerRole ?? 'teacher');
  fixture.componentRef.setInput('canSend', true);
  fixture.componentRef.setInput('sending', inputs.sending ?? false);
  fixture.detectChanges();

  const sent: string[] = [];
  fixture.componentInstance.sent.subscribe((text) => sent.push(text));

  const component = fixture.componentInstance as unknown as ComponentAccess & {
    draft: { set: (value: string) => void };
  };
  component.setDraft = (value: string) => component.draft.set(value);

  return { component, sent };
}

describe('Chat', () => {
  describe('visibleMessages', () => {
    it('filters out messages from the system role', () => {
      const { component } = setup({
        messages: [
          makeMessage({ id: 'm1', authorRole: 'student' }),
          makeMessage({ id: 'm2', authorRole: 'system', messageType: 'faqAnswer' }),
          makeMessage({ id: 'm3', authorRole: 'teacher', messageType: 'teacherAnswer' }),
        ],
      });

      expect(component.visibleMessages().map((m) => m.id)).toEqual(['m1', 'm3']);
    });

    it('sorts messages by sequence number ascending', () => {
      const { component } = setup({
        messages: [
          makeMessage({ id: 'm3', sequenceNumber: 3 }),
          makeMessage({ id: 'm1', sequenceNumber: 1 }),
          makeMessage({ id: 'm2', sequenceNumber: 2 }),
        ],
      });

      expect(component.visibleMessages().map((m) => m.id)).toEqual(['m1', 'm2', 'm3']);
    });

    it('returns an empty list when there are no messages', () => {
      const { component } = setup();
      expect(component.visibleMessages()).toEqual([]);
    });
  });

  describe('submitDraft', () => {
    it('does not emit when the draft is empty', () => {
      const { component, sent } = setup();
      component.submitDraft();
      expect(sent).toEqual([]);
    });

    it('does not emit when the draft is only whitespace', () => {
      const { component, sent } = setup();
      component.setDraft('   ');
      component.submitDraft();
      expect(sent).toEqual([]);
    });

    it('does not emit while sending', () => {
      const { component, sent } = setup({ sending: true });
      component.setDraft('Hi');
      component.submitDraft();
      expect(sent).toEqual([]);
    });

    it('emits the trimmed draft and clears it', () => {
      const { component, sent } = setup();
      component.setDraft('  Hej med dig  ');

      component.submitDraft();

      expect(sent).toEqual(['Hej med dig']);
      expect(component.draft()).toBe('');
    });
  });

  describe('onSubmit', () => {
    it('prevents the default form submission and delegates to submitDraft', () => {
      const { component, sent } = setup();
      component.setDraft('Hej');

      const preventDefault = vi.fn();
      component.onSubmit({ preventDefault } as unknown as Event);

      expect(preventDefault).toHaveBeenCalled();
      expect(sent).toEqual(['Hej']);
    });
  });
});

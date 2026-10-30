import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import {
  HelpRequestApi,
  type HelpRequestDetail,
  type HelpRequestStatus,
} from '../../../help-request';
import { SessionApi } from '../../../session';
import { fakeResource } from '../../../../../test-utils/fake-resource';
import { AnswerQuestion } from './answer-question';

type AnswerViewState =
  | 'loading'
  | 'digital-chat'
  | 'faq-answer'
  | 'physical-prepare'
  | 'physical-active'
  | 'closed'
  | 'unsupported';

interface ComponentAccess {
  viewState: () => AnswerViewState;
}

function access(component: AnswerQuestion): ComponentAccess {
  return component as unknown as ComponentAccess;
}

function makeDetail(overrides: Partial<HelpRequestDetail> = {}): HelpRequestDetail {
  return {
    id: 'request-1',
    displayNumber: 'BF-001',
    isFaq: false,
    status: 'pending',
    helpType: 'physical',
    requestNumber: 1,
    queuedAt: '2026-01-01T10:00:00Z',
    assignedTeacherName: null,
    messages: [],
    question: 'Hvordan starter jeg Docker?',
    ...overrides,
  };
}

function setup(detail: HelpRequestDetail | undefined): ComponentAccess {
  const requestResource = fakeResource(detail);
  const sessionResource = fakeResource({
    id: 'session-1',
    title: 'SWBAD',
    currentGeneration: 1,
    lastClearedAtUtc: null,
  });

  const helpRequestApi = {
    getTeacherRequest: vi.fn(() => requestResource),
    answerTeacherRequest: vi.fn(),
    changeTeacherStatus: vi.fn(),
  };
  const sessionApi = {
    getTeacherSession: vi.fn(() => sessionResource),
  };

  TestBed.configureTestingModule({
    providers: [
      { provide: Router, useValue: { navigate: vi.fn() } },
      { provide: ActivatedRoute, useValue: { snapshot: { params: { id: 'session-1' } } } },
      { provide: HelpRequestApi, useValue: helpRequestApi },
      { provide: SessionApi, useValue: sessionApi },
    ],
  });

  const fixture = TestBed.createComponent(AnswerQuestion);
  fixture.componentRef.setInput('id', 'session-1');
  fixture.componentRef.setInput('requestId', 'request-1');
  fixture.detectChanges();

  return access(fixture.componentInstance);
}

describe('AnswerQuestion', () => {
  describe('view state', () => {
    it('returns loading while the request detail is not yet available', () => {
      const component = setup(undefined);
      expect(component.viewState()).toBe('loading');
    });

    it.each<{ status: HelpRequestStatus }>([
      { status: 'completedByDigital' },
      { status: 'completedByPhysical' },
      { status: 'completedByFaq' },
      { status: 'cancelled' },
    ])('maps $status to closed', ({ status }) => {
      const component = setup(makeDetail({ status }));
      expect(component.viewState()).toBe('closed');
    });

    it.each<{ status: HelpRequestStatus }>([
      { status: 'awaitingFaq' },
      { status: 'awaitingConfirmation' },
    ])('maps $status to unsupported', ({ status }) => {
      const component = setup(makeDetail({ status }));
      expect(component.viewState()).toBe('unsupported');
    });

    it('maps a pending physical request to physical-prepare', () => {
      const component = setup(makeDetail({ helpType: 'physical', status: 'pending' }));
      expect(component.viewState()).toBe('physical-prepare');
    });

    it('maps an active physical request to physical-active', () => {
      const component = setup(makeDetail({ helpType: 'physical', status: 'active' }));
      expect(component.viewState()).toBe('physical-active');
    });

    it('maps a digital request to digital-chat regardless of active status', () => {
      const component = setup(makeDetail({ helpType: 'digital', status: 'active' }));
      expect(component.viewState()).toBe('digital-chat');
    });

    it('prefers faq-answer over digital-chat when the request is a FAQ', () => {
      const component = setup(
        makeDetail({ isFaq: true, helpType: 'digital', status: 'pending' }),
      );
      expect(component.viewState()).toBe('faq-answer');
    });

    it('prefers faq-answer over physical-active when the request is a FAQ', () => {
      const component = setup(
        makeDetail({ isFaq: true, helpType: 'physical', status: 'active' }),
      );
      expect(component.viewState()).toBe('faq-answer');
    });

    it('treats closed as taking precedence over the FAQ flag', () => {
      const component = setup(makeDetail({ isFaq: true, status: 'completedByFaq' }));
      expect(component.viewState()).toBe('closed');
    });

    it('treats unsupported statuses as taking precedence over the FAQ flag', () => {
      const component = setup(makeDetail({ isFaq: true, status: 'awaitingConfirmation' }));
      expect(component.viewState()).toBe('unsupported');
    });
  });
});

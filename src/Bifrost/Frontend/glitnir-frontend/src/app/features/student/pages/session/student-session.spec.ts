import { HttpResourceRef } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { FaqApi } from '../../../faq/services/faq-api';
import { HelpRequest, HelpRequestStatus, HelpType } from '../../../help-request/help-request.model';
import { HelpRequestApi } from '../../../help-request/services/help-request-api';
import { SessionApi } from '../../../session/services/session-api';
import { StudentSession } from './student-session';

function fakeResource<T>(initial: T): HttpResourceRef<T> {
  return {
    value: signal(initial).asReadonly(),
    isLoading: signal(false).asReadonly(),
    error: signal(null).asReadonly(),
    reload: () => {},
  } as unknown as HttpResourceRef<T>;
}

function makeRequest(index: number) {
  return {
    id: `request-${index}`,
    displayNumber: `BF-${String(index).padStart(3, '0')}`,
    status: 'pending' as const,
    message: `Question ${index}`,
    helpType: 'physical' as const,
    requestNumber: index,
  };
}

function makeRequestWith(status: HelpRequestStatus, helpType: HelpType): HelpRequest {
  return {
    id: 'request-1',
    displayNumber: 'BF-001',
    status,
    message: 'Question',
    helpType,
    requestNumber: 1,
  };
}

describe('StudentSession', () => {
  let routerMock: { navigate: ReturnType<typeof vi.fn>; navigateByUrl: ReturnType<typeof vi.fn> };

  function setup(questionCount: number): ComponentFixture<StudentSession> {
    routerMock = { navigate: vi.fn(), navigateByUrl: vi.fn() };

    const sessionApi = {
      getStudentSession: () =>
        fakeResource({
          id: 'session-1',
          title: 'SWBAD',
          currentGeneration: 1,
          lastClearedAtUtc: null,
        }),
      getSessionStatus: () => signal({ waitingCount: 0, estimatedWaitMinutes: 0 }),
      getStudentGenerations: () => signal([]),
    };

    const helpRequestApi = {
      getStudentRequests: () =>
        fakeResource(Array.from({ length: questionCount }, (_, i) => makeRequest(i))),
      getStudentQueue: () => fakeResource({ ownRequestId: null }),
      createHelpRequest: () => of(makeRequest(0)),
    };

    const faqApi = {
      getSessionFaq: () => fakeResource([]),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: { snapshot: { params: { id: 'session-1' } } } },
        { provide: SessionApi, useValue: sessionApi },
        { provide: HelpRequestApi, useValue: helpRequestApi },
        { provide: FaqApi, useValue: faqApi },
      ],
    });

    const fixture = TestBed.createComponent(StudentSession);
    fixture.componentRef.setInput('id', 'session-1');
    fixture.detectChanges();
    return fixture;
  }

  describe('archiveVisibleCount', () => {
    it.each([
      { questions: 0, expected: 3 },
      { questions: 1, expected: 3 },
      { questions: 3, expected: 3 },
      { questions: 5, expected: 4 },
      { questions: 10, expected: 9 },
    ])(
      'returns $expected archive slots for $questions questions',
      ({ questions, expected }) => {
        const fixture = setup(questions);
        const component = fixture.componentInstance as unknown as {
          archiveVisibleCount: () => number;
        };
        expect(component.archiveVisibleCount()).toBe(expected);
      },
    );
  });

  describe('onQuestionSelected', () => {
    type ComponentAccess = {
      onQuestionSelected: (request: HelpRequest) => void;
      selectedRequest: () => HelpRequest | null;
    };

    it('opens the conversation dialog for completed digital requests', () => {
      const fixture = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;
      const request = makeRequestWith('completed', 'digital');

      component.onQuestionSelected(request);

      expect(component.selectedRequest()).toBe(request);
      expect(routerMock.navigate).not.toHaveBeenCalled();
    });

    it('navigates to the queue for pending requests', () => {
      const fixture = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.onQuestionSelected(makeRequestWith('pending', 'physical'));

      expect(routerMock.navigate).toHaveBeenCalledWith(['queue'], expect.anything());
    });

    it('navigates to the queue for active requests', () => {
      const fixture = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.onQuestionSelected(makeRequestWith('active', 'physical'));

      expect(routerMock.navigate).toHaveBeenCalledWith(['queue'], expect.anything());
    });

    it('navigates to the queue for awaiting confirmation requests', () => {
      const fixture = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.onQuestionSelected(makeRequestWith('awaitingConfirmation', 'digital'));

      expect(routerMock.navigate).toHaveBeenCalledWith(['queue'], expect.anything());
    });

    it('does nothing for completed physical requests', () => {
      const fixture = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.onQuestionSelected(makeRequestWith('completed', 'physical'));

      expect(component.selectedRequest()).toBeNull();
      expect(routerMock.navigate).not.toHaveBeenCalled();
    });
  });
});

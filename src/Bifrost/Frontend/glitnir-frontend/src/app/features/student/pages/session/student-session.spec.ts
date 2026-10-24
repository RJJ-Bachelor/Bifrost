import { HttpResourceRef } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { FaqApi } from '../../../faq/services/faq-api';
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

describe('StudentSession', () => {
  function setup(questionCount: number): ComponentFixture<StudentSession> {
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
        provideRouter([]),
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
});

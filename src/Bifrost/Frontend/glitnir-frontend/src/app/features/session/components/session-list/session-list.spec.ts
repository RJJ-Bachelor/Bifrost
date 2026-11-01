import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Session } from '../../session.model';
import { SessionList } from './session-list';

interface SessionListInternals {
  showWaiting: () => boolean;
}

function makeSession(overrides: Partial<Session> = {}): Session {
  return {
    id: 'session-1',
    title: 'SWBAD-26',
    currentGeneration: 1,
    lastClearedAtUtc: null,
    ...overrides,
  };
}

describe('SessionList', () => {
  function setup(sessions: Session[]): {
    fixture: ComponentFixture<SessionList>;
    component: SessionListInternals;
  } {
    const fixture = TestBed.createComponent(SessionList);
    fixture.componentRef.setInput('sessions', sessions);
    fixture.detectChanges();

    return {
      fixture,
      component: fixture.componentInstance as unknown as SessionListInternals,
    };
  }

  describe('showWaiting', () => {
    it('is false when no session carries waiting counts (student view)', () => {
      const { component } = setup([makeSession()]);
      expect(component.showWaiting()).toBe(false);
    });

    it('is true when any session carries waiting counts (teacher view)', () => {
      const { component } = setup([makeSession({ waitingCount: 0 })]);
      expect(component.showWaiting()).toBe(true);
    });
  });
});

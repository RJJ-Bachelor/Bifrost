import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ArchiveApi } from './archive-api';

describe('ArchiveApi', () => {
  let service: ArchiveApi;
  let http: HttpTestingController;

  const emptyDetail = {
    id: 'archive-1',
    sessionId: 'session-1',
    title: 'SQL vs Mongo',
    generation: 1,
    startedAtUtc: '2026-10-01T10:00:00Z',
    archivedAtUtc: '2026-10-08T10:00:00Z',
    faqs: [],
    conversations: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ArchiveApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  describe('getArchive', () => {
    it('requests the teacher endpoint when viewerRole is teacher', async () => {
      TestBed.runInInjectionContext(() =>
        service.getArchive(
          () => 'teacher',
          () => 'session-1',
          () => 'archive-1',
        ),
      );
      TestBed.tick();

      http
        .expectOne('/api/teachers/sessions/session-1/archives/archive-1')
        .flush(emptyDetail);

      await TestBed.inject(ApplicationRef).whenStable();
    });

    it('requests the student endpoint when viewerRole is student', async () => {
      TestBed.runInInjectionContext(() =>
        service.getArchive(
          () => 'student',
          () => 'session-1',
          () => 'archive-1',
        ),
      );
      TestBed.tick();

      http
        .expectOne('/api/students/sessions/session-1/archives/archive-1')
        .flush(emptyDetail);

      await TestBed.inject(ApplicationRef).whenStable();
    });
  });
});

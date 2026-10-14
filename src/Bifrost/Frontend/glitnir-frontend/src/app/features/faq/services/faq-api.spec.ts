import { TestBed } from '@angular/core/testing';
import { FaqApi } from './faq-api';

describe('FaqApi', () => {
  let service: FaqApi;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(FaqApi);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});

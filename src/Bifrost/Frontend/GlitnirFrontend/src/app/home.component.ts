import { Component } from '@angular/core';

@Component({
  selector: 'app-home',
  template: `
    <section class="panel welcome">
      <p class="eyebrow">GLITNIR</p>
      <h1>Velkommen</h1>
      <p class="intro">En enkel test af elev- og læreradgang.</p>
      <p>Forsiden henter ingen data. Vælg Student eller Teacher i menuen for at teste adgangen.</p>
    </section>
  `
})
export class HomeComponent { }

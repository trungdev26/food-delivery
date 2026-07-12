import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

@Component({
    selector: 'app-main-layout',
    imports: [RouterLink, RouterOutlet],
    templateUrl: './main-layout.component.html',
})
export class MainLayoutComponent {}

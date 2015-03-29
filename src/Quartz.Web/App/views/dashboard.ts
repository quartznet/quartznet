import {autoinject} from 'aurelia-framework';
import {Parent} from "aurelia-framework";
import {Router} from "aurelia-router";
import {HttpClient} from "aurelia-http-client";

@autoinject
export class Dashboard {

    public heading: string;
    public schedulers: any[];

    constructor(private  router: Router, private http: HttpClient) {
        this.heading = "Dashboard";
    }

    activate() {
        return this.http.get("/api/schedulers").then(response => {
            this.schedulers = <any[]>response.content;
        });
    }
}
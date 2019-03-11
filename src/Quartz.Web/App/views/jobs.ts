import {autoinject} from 'aurelia-framework';
import {Parent} from "aurelia-framework";
import {Router} from "aurelia-router";
import {HttpClient} from "aurelia-http-client";

@autoinject
export class JobsView {

    public jobs: any[];

    constructor(private router: Router, private http: HttpClient) {
    }

    activate(params: any) {
        return this.http.get(`/api/schedulers/${params.schedulerName}/jobs`).then(response => {
            this.jobs = <any[]>response.content;
        });
    }
}
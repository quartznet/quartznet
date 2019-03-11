import {autoinject} from 'aurelia-framework';
import {Parent} from "aurelia-framework";
import {Router} from "aurelia-router";
import {HttpClient} from "aurelia-http-client";

@autoinject
export class TriggersView {

    public triggers: any[];

    constructor(private http: HttpClient) {
    }

    activate(params: any) {
        return this.http.get(`/api/schedulers/${params.schedulerName}/triggers`).then(response => {
            this.triggers = <any[]>response.content;
        });
    }
}
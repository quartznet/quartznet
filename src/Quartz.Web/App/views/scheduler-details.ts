import {autoinject} from 'aurelia-framework';
import {Parent} from "aurelia-framework";
import {Router} from "aurelia-router";
import {HttpClient} from "aurelia-http-client";

@autoinject
export class SchedulerIndexView {
    schedulerName: string;
    details: any;
    currentlyExecutingJobs: any[];
    loadingCurrentlyExecutingJobs = false;

    constructor(private router: Router, private http: HttpClient) {
    }

    currentlyExistingJobsExist() {
        return this.currentlyExecutingJobs && this.currentlyExecutingJobs.length > 0;
    }

    activate(params: any) {
        this.schedulerName = params.schedulerName;
        return this.loadDetails();
    }

    standby() {
        this.postCommand("standby");
    }

    start() {
        this.postCommand("start");
    }

    shutdown() {
        this.postCommand("shutdown");
    }

    postCommand(command: string) {
        return this.http.post(`/api/schedulers/${this.schedulerName}/${command}`, null).then(() => {
            return this.loadDetails();
        });
    }

    loadDetails() {
        return $.when(
            this.http.get(`/api/schedulers/${this.schedulerName}`).then(response => {
                this.details = response.content;
            }),
            this.refreshCurrentlyExecutingJobs()
        );
    }

    refreshCurrentlyExecutingJobs() {
        this.loadingCurrentlyExecutingJobs = true;
        return this.http.get(`/api/schedulers/${this.schedulerName}/jobs/currently-executing`)
            .then(response => {
                this.currentlyExecutingJobs = response.content;
            })
            .catch(() => {})
            .then(() => {
                this.loadingCurrentlyExecutingJobs = false;
            });
    }
}
import {Router} from 'aurelia-router';

export class SchedulerRouter {
    router: Router;
    heading: string;

    configureRouter(config, router: Router) {
        this.heading = "Scheduler";
        config.map([
            { route: ["", "details"], moduleId: "./scheduler-details", nav: true, title: "Details" },
            { route: "triggers", moduleId: "./triggers", nav: true, title: "Triggers" },
            { route: "triggers/:group/:name/details", name: "trigger-details", moduleId: "./trigger-details", nav: false },
            { route: "jobs", moduleId: "./jobs", nav: true, title: "Jobs" },
            { route: "jobs/:group/:name", name: "job-details", moduleId: "./job-details", nav: false },
            { route: "history", moduleId: "./history", nav: true, title: "History" }
        ]);
        this.router = router;
    }
}
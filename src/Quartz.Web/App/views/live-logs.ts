import {autoinject, singleton} from 'aurelia-framework';
import {Parent} from "aurelia-framework";
import {Router} from "aurelia-router";
import {HttpClient} from "aurelia-http-client";

@autoinject
export class LiveLogsView {
    private liveLogHub: HubProxy;
    entries = [];
    numberOfEntriesToShow: string = "50";
    showJobInfo = true;
    showTriggerInfo = true;

    constructor(private router: Router, private http: HttpClient) {
        this.liveLogHub = (<any>$.connection).liveLogHub;
        this.liveLogHub
            .on("triggerFired", (trigger) => {
                if (this.showTriggerInfo)
                    this.showMessage(`Trigger <strong>${trigger.Name}.${trigger.Group}</strong> fired`);
            })
            .on("triggerMisfired", (trigger) => {
                if (this.showTriggerInfo)
                    this.showMessage(`<strong>Trigger ${trigger.Name}.${trigger.Group} misfired</strong>`);
            })
            .on("triggerCompleted", (trigger) => {
                if (this.showTriggerInfo)
                    this.showMessage(`Trigger <strong>${trigger.Name}.${trigger.Group}</strong> has completed`);
            })
            .on("triggerPaused", (triggerKey) => {
                if (this.showTriggerInfo)
                    this.showMessage(`Trigger <strong>${triggerKey.Name}.${triggerKey.Group}</strong> was paused`);
            })
            .on("triggerResumed", (triggerKey) => {
                if (this.showTriggerInfo)
                    this.showMessage(`Trigger <strong>${triggerKey.Name}.${triggerKey.Group}</strong> was resumed`);
            })
            .on("jobPaused", (jobKey) => {
                if (this.showJobInfo)
                    this.showMessage(`Job <strong>${jobKey.Name}.${jobKey.Group}</strong> was paused`);
            })
            .on("jobResumed", (jobKey) => {
                if (this.showJobInfo)
                    this.showMessage(`Job <strong>${jobKey.Name}.${jobKey.Group}</strong> was resumed`);
            })
            .on("jobToBeExecuted", (jobKey, triggerKey) => {
                if (this.showJobInfo)
                    this.showMessage(`Starting to execute job <strong>${jobKey.Name}.${jobKey.Group}</strong> triggered by trigger <strong>${triggerKey.Name}.${triggerKey.Group}</strong>...`);
            })
            .on("jobWasExecuted", (jobKey, triggerKey, errorMessage) => {
                if (this.showJobInfo) {
                    let message = `Job <strong>${jobKey.Name}.${jobKey.Group}</strong> was executed`;
                    if (errorMessage) {
                        message += " and ended with error: " + errorMessage;
                    }
                    this.showMessage(message);
                }
            });

        $.connection.hub.error(error => {
            this.showMessage(error);
        });
    }

    attached() {
        this.showMessage("Connecting to hub...");
        // Start the connection
        $.connection.hub.start()
            .then(() => {
                this.showMessage("Connected");
            })
            .fail(() => {
                this.showMessage("SignalR error: Could not start hub connection");
            });
    }

    private showMessage(message: string) {

        while (this.entries.length >= parseInt(this.numberOfEntriesToShow)) {
            this.entries.shift();
        }

        let value = {
            date: moment(),
            message: message
        };
        this.entries.push(value);
    }

    deactivate() {
        if (this.liveLogHub && this.liveLogHub.connection) {
            this.liveLogHub.connection.stop();
        }
    }
}
import {autoinject} from 'aurelia-framework';
import {Parent} from "aurelia-framework";
import {Router} from "aurelia-router";
import {HttpClient} from "aurelia-http-client";
import * as toastr from "toastr";
import * as bootbox from "bootbox";
import * as bootstrap from "bootstrap";

@autoinject
export class CalendarDetailsView {
    schedulerName: string;
    name: string;
    details: any;

    constructor(private router: Router, private http: HttpClient) {
    }

    activate(params: any) {
        this.schedulerName = params.schedulerName;
        this.name = params.name;
        return this.loadDetails();
    }

    delete() {
        bootbox.confirm({
            size: "small",
            message: `Delete ${this.name}?`,
            callback: (result: boolean) => {
                if (result) {
                    return this.http.delete(`/api/schedulers/${this.schedulerName}/calendars/${this.name}`).then(() => {
                        toastr.success(`Calendar ${this.name} deleted successfully`);
                        return this.router.navigate("calendars");
                    });
                }
                return $.when();
            }
        });
    }

    loadDetails() {
        return this.http.get(`/api/schedulers/${this.schedulerName}/calendars/${this.name}`).then(response => {
            this.details = response.content;
        });
    }

    isAnnualCalendar() {
        return this.calendarTypeNameContains("AnnualCalendar");
    }

    isCronCalendar() {
        return this.calendarTypeNameContains("CronCalendar");
    }

    isDailyCalendar() {
        return this.calendarTypeNameContains("DailyCalendar");
    }

    isHolidayCalendar() {
        return this.calendarTypeNameContains("HolidayCalendar");
    }

    isMonthlyCalendar() {
        return this.calendarTypeNameContains("MonthlyCalendar");
    }

    isWeeklyCalendar() {
        return this.calendarTypeNameContains("WeeklyCalendar");
    }

    calendarTypeNameContains(name: string): boolean {
        return this.details && this.details.calendarType.indexOf(name) > -1;
    }
}
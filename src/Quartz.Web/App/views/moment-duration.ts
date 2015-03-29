import {customAttribute, inject} from "aurelia-framework";

@customAttribute("moment-duration", null)
@inject(Element)
export class MomentDuration {
    private element: HTMLElement;

    constructor(element) {
        this.element = element;
    }

    valueChanged(newValue) {
        this.element.textContent = newValue ? moment.duration(newValue).seconds().toString() : "";
    }
}
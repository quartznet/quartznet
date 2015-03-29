import {customAttribute, inject} from "aurelia-framework";

@customAttribute("moment-datetime", null)
@inject(Element)
export class MomentDateTime {
    private element: HTMLElement;

    constructor(element) {
        this.element = element;
    }

    valueChanged(newValue) {
        this.element.textContent = newValue ? moment(newValue).format() : "";
    }
}
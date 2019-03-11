export class DateFormatValueConverter {
    toView(value, format) {
        if (!value) {
            return "";
        }
        return moment(value).format(format);
    }
}
export class DurationFormatValueConverter {
    toView(value, format) {
        if (!value) {
            return "";
        }
        return moment.utc(moment.duration(value).asMilliseconds()).format("HH:mm:ss.SSS");
    }
}
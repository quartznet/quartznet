System.config({
  baseURL: "/",
  defaultJSExtensions: true,
  transpiler: "babel",
  babelOptions: {
    "optional": [
      "runtime",
      "optimisation.modules.system"
    ]
  },
  paths: {
    "github:*": "jspm_packages/github/*",
    "npm:*": "jspm_packages/npm/*"
  },

  map: {
    "aurelia-animator-css": "github:aurelia/animator-css@0.16.0",
    "aurelia-binding": "github:aurelia/binding@0.9.1",
    "aurelia-bootstrapper": "github:aurelia/bootstrapper@0.17.0",
    "aurelia-dependency-injection": "github:aurelia/dependency-injection@0.10.1",
    "aurelia-event-aggregator": "github:aurelia/event-aggregator@0.8.0",
    "aurelia-framework": "github:aurelia/framework@0.16.0",
    "aurelia-history": "github:aurelia/history@0.7.0",
    "aurelia-history-browser": "github:aurelia/history-browser@0.8.0",
    "aurelia-http-client": "github:aurelia/http-client@0.11.0",
    "aurelia-loader": "github:aurelia/loader@0.9.0",
    "aurelia-loader-default": "github:aurelia/loader-default@0.10.0",
    "aurelia-logging": "github:aurelia/logging@0.7.0",
    "aurelia-logging-console": "github:aurelia/logging-console@0.6.2",
    "aurelia-metadata": "github:aurelia/metadata@0.8.0",
    "aurelia-path": "github:aurelia/path@0.9.0",
    "aurelia-route-recognizer": "github:aurelia/route-recognizer@0.7.0",
    "aurelia-router": "github:aurelia/router@0.12.0",
    "aurelia-task-queue": "github:aurelia/task-queue@0.7.0",
    "aurelia-templating": "github:aurelia/templating@0.15.3",
    "aurelia-templating-binding": "github:aurelia/templating-binding@0.15.0",
    "aurelia-templating-resources": "github:aurelia/templating-resources@0.15.2",
    "aurelia-templating-router": "github:aurelia/templating-router@0.16.1",
    "aurelia-validation": "github:aurelia/validation@0.3.1",
    "babel": "npm:babel-core@5.8.34",
    "babel-runtime": "npm:babel-runtime@5.8.34",
    "bootbox": "npm:bootbox@4.4.0",
    "bootstrap": "github:twbs/bootstrap@3.3.5",
    "core-js": "npm:core-js@1.1.4",
    "font-awesome": "npm:font-awesome@4.4.0",
    "jquery": "github:components/jquery@2.1.4",
    "moment": "github:moment/moment@2.10.6",
    "systemjs": "npm:systemjs@0.19.0",
    "toastr": "github:CodeSeven/toastr@2.1.2",
    "traceur": "github:jmcriffey/bower-traceur@0.0.87",
    "traceur-runtime": "github:jmcriffey/bower-traceur-runtime@0.0.87",
    "github:CodeSeven/toastr@2.1.2": {
      "css": "github:systemjs/plugin-css@0.1.17",
      "jquery": "github:components/jquery@2.1.4"
    },
    "github:aurelia/animator-css@0.16.0": {
      "aurelia-metadata": "github:aurelia/metadata@0.8.0",
      "aurelia-templating": "github:aurelia/templating@0.15.3"
    },
    "github:aurelia/binding@0.9.1": {
      "aurelia-dependency-injection": "github:aurelia/dependency-injection@0.10.1",
      "aurelia-metadata": "github:aurelia/metadata@0.8.0",
      "aurelia-task-queue": "github:aurelia/task-queue@0.7.0",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/bootstrapper@0.17.0": {
      "aurelia-event-aggregator": "github:aurelia/event-aggregator@0.8.0",
      "aurelia-framework": "github:aurelia/framework@0.16.0",
      "aurelia-history": "github:aurelia/history@0.7.0",
      "aurelia-history-browser": "github:aurelia/history-browser@0.8.0",
      "aurelia-loader-default": "github:aurelia/loader-default@0.10.0",
      "aurelia-logging-console": "github:aurelia/logging-console@0.7.1",
      "aurelia-router": "github:aurelia/router@0.12.0",
      "aurelia-templating": "github:aurelia/templating@0.15.3",
      "aurelia-templating-binding": "github:aurelia/templating-binding@0.15.0",
      "aurelia-templating-resources": "github:aurelia/templating-resources@0.15.2",
      "aurelia-templating-router": "github:aurelia/templating-router@0.16.1",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/dependency-injection@0.10.1": {
      "aurelia-logging": "github:aurelia/logging@0.7.0",
      "aurelia-metadata": "github:aurelia/metadata@0.8.0",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/event-aggregator@0.8.0": {
      "aurelia-logging": "github:aurelia/logging@0.7.0"
    },
    "github:aurelia/framework@0.16.0": {
      "aurelia-binding": "github:aurelia/binding@0.9.1",
      "aurelia-dependency-injection": "github:aurelia/dependency-injection@0.10.1",
      "aurelia-loader": "github:aurelia/loader@0.9.0",
      "aurelia-logging": "github:aurelia/logging@0.7.0",
      "aurelia-metadata": "github:aurelia/metadata@0.8.0",
      "aurelia-path": "github:aurelia/path@0.9.0",
      "aurelia-task-queue": "github:aurelia/task-queue@0.7.0",
      "aurelia-templating": "github:aurelia/templating@0.15.3",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/history-browser@0.8.0": {
      "aurelia-history": "github:aurelia/history@0.7.0",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/http-client@0.11.0": {
      "aurelia-path": "github:aurelia/path@0.9.0",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/loader-default@0.10.0": {
      "aurelia-loader": "github:aurelia/loader@0.9.0",
      "aurelia-metadata": "github:aurelia/metadata@0.8.0"
    },
    "github:aurelia/loader@0.9.0": {
      "aurelia-html-template-element": "github:aurelia/html-template-element@0.3.0",
      "aurelia-metadata": "github:aurelia/metadata@0.8.0",
      "aurelia-path": "github:aurelia/path@0.9.0",
      "core-js": "github:zloirock/core-js@0.8.4"
    },
    "github:aurelia/logging-console@0.6.2": {
      "aurelia-logging": "github:aurelia/logging@0.6.4"
    },
    "github:aurelia/logging-console@0.7.1": {
      "aurelia-logging": "github:aurelia/logging@0.7.0"
    },
    "github:aurelia/metadata@0.8.0": {
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/route-recognizer@0.7.0": {
      "aurelia-path": "github:aurelia/path@0.9.0",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/router@0.12.0": {
      "aurelia-dependency-injection": "github:aurelia/dependency-injection@0.10.1",
      "aurelia-event-aggregator": "github:aurelia/event-aggregator@0.8.0",
      "aurelia-history": "github:aurelia/history@0.7.0",
      "aurelia-logging": "github:aurelia/logging@0.7.0",
      "aurelia-path": "github:aurelia/path@0.9.0",
      "aurelia-route-recognizer": "github:aurelia/route-recognizer@0.7.0",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/templating-binding@0.15.0": {
      "aurelia-binding": "github:aurelia/binding@0.9.1",
      "aurelia-logging": "github:aurelia/logging@0.7.0",
      "aurelia-templating": "github:aurelia/templating@0.15.3"
    },
    "github:aurelia/templating-resources@0.15.2": {
      "aurelia-binding": "github:aurelia/binding@0.9.1",
      "aurelia-dependency-injection": "github:aurelia/dependency-injection@0.10.1",
      "aurelia-loader": "github:aurelia/loader@0.9.0",
      "aurelia-logging": "github:aurelia/logging@0.7.0",
      "aurelia-path": "github:aurelia/path@0.9.0",
      "aurelia-task-queue": "github:aurelia/task-queue@0.7.0",
      "aurelia-templating": "github:aurelia/templating@0.15.3",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/templating-router@0.16.1": {
      "aurelia-dependency-injection": "github:aurelia/dependency-injection@0.10.1",
      "aurelia-metadata": "github:aurelia/metadata@0.8.0",
      "aurelia-path": "github:aurelia/path@0.9.0",
      "aurelia-router": "github:aurelia/router@0.12.0",
      "aurelia-templating": "github:aurelia/templating@0.15.3"
    },
    "github:aurelia/templating@0.15.3": {
      "aurelia-binding": "github:aurelia/binding@0.9.1",
      "aurelia-dependency-injection": "github:aurelia/dependency-injection@0.10.1",
      "aurelia-html-template-element": "github:aurelia/html-template-element@0.3.0",
      "aurelia-loader": "github:aurelia/loader@0.9.0",
      "aurelia-logging": "github:aurelia/logging@0.7.0",
      "aurelia-metadata": "github:aurelia/metadata@0.8.0",
      "aurelia-path": "github:aurelia/path@0.9.0",
      "aurelia-task-queue": "github:aurelia/task-queue@0.7.0",
      "core-js": "npm:core-js@0.9.18"
    },
    "github:aurelia/validation@0.3.1": {
      "aurelia-binding": "github:aurelia/binding@0.9.1",
      "aurelia-dependency-injection": "github:aurelia/dependency-injection@0.10.1",
      "aurelia-logging": "github:aurelia/logging@0.7.0",
      "aurelia-metadata": "github:aurelia/metadata@0.8.0",
      "aurelia-templating": "github:aurelia/templating@0.15.3"
    },
    "github:jspm/nodelibs-assert@0.1.0": {
      "assert": "npm:assert@1.3.0"
    },
    "github:jspm/nodelibs-buffer@0.1.0": {
      "buffer": "npm:buffer@3.5.0"
    },
    "github:jspm/nodelibs-process@0.1.2": {
      "process": "npm:process@0.11.2"
    },
    "github:jspm/nodelibs-util@0.1.0": {
      "util": "npm:util@0.10.3"
    },
    "github:twbs/bootstrap@3.3.5": {
      "jquery": "github:components/jquery@2.1.4"
    },
    "npm:assert@1.3.0": {
      "util": "npm:util@0.10.3"
    },
    "npm:babel-runtime@5.8.34": {
      "process": "github:jspm/nodelibs-process@0.1.2"
    },
    "npm:bootbox@4.4.0": {
      "process": "github:jspm/nodelibs-process@0.1.2"
    },
    "npm:buffer@3.5.0": {
      "base64-js": "npm:base64-js@0.0.8",
      "ieee754": "npm:ieee754@1.1.6",
      "is-array": "npm:is-array@1.0.1"
    },
    "npm:core-js@0.9.18": {
      "fs": "github:jspm/nodelibs-fs@0.1.2",
      "process": "github:jspm/nodelibs-process@0.1.2",
      "systemjs-json": "github:systemjs/plugin-json@0.1.0"
    },
    "npm:core-js@1.1.4": {
      "fs": "github:jspm/nodelibs-fs@0.1.2",
      "process": "github:jspm/nodelibs-process@0.1.2",
      "systemjs-json": "github:systemjs/plugin-json@0.1.0"
    },
    "npm:es6-module-loader@0.17.7": {
      "fs": "github:jspm/nodelibs-fs@0.1.2",
      "process": "github:jspm/nodelibs-process@0.1.2",
      "systemjs-json": "github:systemjs/plugin-json@0.1.0",
      "util": "github:jspm/nodelibs-util@0.1.0",
      "when": "npm:when@3.7.3"
    },
    "npm:font-awesome@4.4.0": {
      "css": "github:systemjs/plugin-css@0.1.17"
    },
    "npm:inherits@2.0.1": {
      "util": "github:jspm/nodelibs-util@0.1.0"
    },
    "npm:process@0.11.2": {
      "assert": "github:jspm/nodelibs-assert@0.1.0"
    },
    "npm:systemjs@0.19.0": {
      "buffer": "github:jspm/nodelibs-buffer@0.1.0",
      "es6-module-loader": "npm:es6-module-loader@0.17.7",
      "fs": "github:jspm/nodelibs-fs@0.1.2",
      "process": "github:jspm/nodelibs-process@0.1.2",
      "systemjs-json": "github:systemjs/plugin-json@0.1.0",
      "when": "npm:when@3.7.3"
    },
    "npm:util@0.10.3": {
      "inherits": "npm:inherits@2.0.1",
      "process": "github:jspm/nodelibs-process@0.1.2"
    },
    "npm:when@3.7.3": {
      "process": "github:jspm/nodelibs-process@0.1.2"
    }
  }
});

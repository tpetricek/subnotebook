#!/usr/bin/env python3
import json
from flask import Blueprint, Flask, request, jsonify
from flask_cors import CORS
import json

class ApiException(Exception):
    status_code = 500

    def __init__(self, message, status_code=None, payload=None):
        Exception.__init__(self)
        self.message = message
        if status_code is not None:
            self.status_code = status_code
        self.payload = payload

    def to_dict(self):
        rv = dict(self.payload or ())
        rv["status"] = "error"
        rv["error"] = self.message
        return rv

def execute_code(code, main):
    print(code)
    print(main)
    try:
        exec(code)
    except SyntaxError as e:
        output = "SyntaxError when trying to execute code in cell: {}".format(e)
        raise ApiException(output, status_code=500)
    try:
        return eval(main + '()')
    except Exception as e:
        output = "{}: {}".format(type(e).__name__, e)
        raise ApiException(output, status_code=500)

python_service_blueprint = Blueprint("python_service", __name__)

@python_service_blueprint.errorhandler(ApiException)
def handle_api_exception(error):
    response = jsonify(error.to_dict())
    response.status_code = error.status_code
    return response

@python_service_blueprint.route("/eval", methods=['POST'])
def eval_request():
    data = json.loads(request.data.decode("utf-8"))
    return jsonify(execute_code(data["code"], data["main"]))

@python_service_blueprint.route("/test", methods=["GET"])
def test():
    return "Python service is alive!"

app = Flask("main")
CORS(app)
app.register_blueprint(python_service_blueprint)
app.run(host='0.0.0.0',port=7101, debug=True, use_reloader=False)
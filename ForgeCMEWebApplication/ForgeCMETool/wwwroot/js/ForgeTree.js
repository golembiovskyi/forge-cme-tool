﻿$(document).ready(function () {
  // first, check if current visitor is signed in
  jQuery.ajax({
    url: '/api/forge/oauth/token',
    success: function (res) {
      // yes, it is signed in...
      $('#signOut').show();
      $('#refreshHubs').show();

      // prepare sign out
      $('#signOut').click(function () {
        $('#hiddenFrame').on('load', function (event) {
          location.href = '/api/forge/oauth/signout';
        });
        $('#hiddenFrame').attr('src', 'https://accounts.autodesk.com/Authentication/LogOut');
        // learn more about this signout iframe at
        // https://forge.autodesk.com/blog/log-out-forge
      })

      // and refresh button
      $('#refreshHubs').click(function () {
        $('#userHubs').jstree(true).refresh();
      });

      // finally:
      prepareUserHubsTree();
      showUser();
    }
  });

  $('#autodeskSigninButton').click(function () {
    jQuery.ajax({
      url: '/api/forge/oauth/url',
      success: function (url) {
        location.href = url;
      }
    });
  })

  $.getJSON("/api/forge/clientid", function (res) {
    $("#ClientID").val(res.id);
    $("#provisionAccountSave").click(function () {
      $('#provisionAccountModal').modal('toggle');
      $('#userHubs').jstree(true).refresh();
    });
  });

  $('#hiddenUploadField').change(function () {
    var node = $('#userHubs').jstree(true).get_selected(true)[0];
    var _this = this;
    if (_this.files.length == 0) return;
    var file = _this.files[0];
    switch (node.type) {
      case 'folders':
        var formData = new FormData();
        formData.append('fileToUpload', file);
        formData.append('folderHref', node.id);
        _this.value = '';

        $.ajax({
          url: '/api/forge/datamanagement',
          data: formData,
          processData: false,
          contentType: false,
          type: 'POST',
          success: function (data) {
            $('#userHubs').jstree(true).refresh_node(node);
            _this.value = '';
          }
        });
        break;
    }
  });
});

function prepareUserHubsTree() {
  var haveBIM360Hub = false;
  $('#userHubs').jstree({
    'core': {
      'themes': { "icons": true },
      'multiple': false,
      'data': {
        "url": '/api/forge/datamanagement',
        "dataType": "json",
        'cache': false,
        'data': function (node) {
          $('#userHubs').jstree(true).toggle_node(node);
          return { "id": node.id };
        },
        "success": function (nodes) {
          nodes.forEach(function (n) {
            if (n.type === 'bim360Hubs' && n.id.indexOf('b.') > 0)
              haveBIM360Hub = true;
          });

          if (!haveBIM360Hub) {
            $("#provisionAccountModal").modal();
            haveBIM360Hub = true;
          }
        }
      }
    },
    'types': {
      'default': {
        'icon': 'glyphicon glyphicon-question-sign'
      },
      '#': {
        'icon': 'glyphicon glyphicon-user'
      },
      'hubs': {
        'icon': 'https://github.com/Autodesk-Forge/learn.forge.viewhubmodels/raw/master/img/a360hub.png'
      },
      'personalHub': {
        'icon': 'https://github.com/Autodesk-Forge/learn.forge.viewhubmodels/raw/master/img/a360hub.png'
      },
      'bim360Hubs': {
        'icon': 'https://github.com/Autodesk-Forge/learn.forge.viewhubmodels/raw/master/img/bim360hub.png'
      },
      'bim360projects': {
        'icon': 'https://github.com/Autodesk-Forge/learn.forge.viewhubmodels/raw/master/img/bim360project.png'
      },
      'a360projects': {
        'icon': 'https://github.com/Autodesk-Forge/learn.forge.viewhubmodels/raw/master/img/a360project.png'
      },
      'items': {
        'icon': 'glyphicon glyphicon-file'
      },
      'bim360documents': {
        'icon': 'glyphicon glyphicon-file'
      },
      'folders': {
        'icon': 'glyphicon glyphicon-folder-open'
      },
      'versions': {
        'icon': 'glyphicon glyphicon-time'
      },
      'unsupported': {
        'icon': 'glyphicon glyphicon-ban-circle'
      }
    },
    "sort": function (a, b) {
      var a1 = this.get_node(a);
      var b1 = this.get_node(b);
      var parent = this.get_node(a1.parent);
      if (parent.type === 'items') {
        var id1 = Number.parseInt(a1.text.substring(a1.text.indexOf('v') + 1, a1.text.indexOf(':')))
        var id2 = Number.parseInt(b1.text.substring(b1.text.indexOf('v') + 1, b1.text.indexOf(':')));
        return id1 > id2 ? 1 : -1;
      }
      else return a1.text > b1.text ? 1 : -1;
    },
    "plugins": ["types", "state", "sort", "contextmenu"],
    "contextmenu": { items: autodeskCustomMenu },
    "state": { "key": "autodeskHubs" }// key restore tree state
  }).bind("activate_node.jstree", function (evt, data) {
    if (data != null && data.node != null && (data.node.type == 'versions' || data.node.type == 'bim360documents')) {
      var urn;
      var viewableId
      if (data.node.id.indexOf('|') > -1) {
        urn = data.node.id.split('|')[1];
        viewableId = data.node.id.split('|')[2];
        launchViewer(urn, viewableId);
      }
      else {
          launchViewer(data.node.id);

          //startConnection(function () {
          //    var formData = new FormData();
          //    formData.append('inputFile', data.node.parent);
          //    //formData.append('data', JSON.stringify({
          //    //    width: $('#width').val(),
          //    //    height: $('#height').val(),
          //   //     activityName: $('#activity').val(),
          //   //     browerConnectionId: connectionId
          //    //}));
          //    writeLog('Uploading input file...');
          //    $.ajax({
          //        url: 'api/forge/designautomation/workitems',
          //        data: formData,
          //        processData: false,
          //        contentType: false,
          //        type: 'POST',
          //        success: function (res) {
          //            writeLog('Workitem started: ' + res.workItemId);
          //        }
          //    });
          //});
          
          $.ajax({
              url: 'api/forge/designautomation/testing',
              contentType: "application/json",
              data: { "id": data.node.parent },
          });
          
      }
    }
  });
}

function autodeskCustomMenu(autodeskNode) {
  var items;

  switch (autodeskNode.type) {
    case "versions":
      var parent = $('#userHubs').jstree(true).get_node(autodeskNode.parent);
      if (parent.text.indexOf('.rvt') == -1) return;
      items = {
        translateIfc: {
          label: "Translate to IFC",
          action: function () {
            jQuery.post({
              url: '/api/forge/modelderivative/jobs',
              contentType: 'application/json',
              data: JSON.stringify({ 'urn': autodeskNode.id, 'output': 'ifc' }),
              success: function (res) {
                $("#forgeViewer").html('Translation started! Please wait and <a href="/api/forge/modelderivative/' +autodeskNode.id + '/ifc" target="_blank">click here</a> to download');
              },
            });
          },
          icon: 'glyphicon glyphicon-cloud-upload'
        }
      };
      break;
    case "folders":
      items = {
        uploadFile: {
          label: "Upload file",
          action: function () {
            uploadFile();
          },
          icon: 'glyphicon glyphicon-cloud-upload'
        }
      }
      break;
  }

  return items;
}

function uploadFile() {
  $('#hiddenUploadField').click();
}

function showUser() {
  jQuery.ajax({
    url: '/api/forge/user/profile',
    success: function (profile) {
      var img = '<img src="' + profile.picture + '" height="30px">';
      $('#userInfo').html(img + profile.name);
    }
  });
}

function startConnection(onReady) {
    if (connection && connection.connectionState) { if (onReady) onReady(); return; }
    connection = new signalR.HubConnectionBuilder().withUrl("/api/signalr/forgecommunication").build();
    connection.start()
        .then(function () {
            connection.invoke('getConnectionId')
                .then(function (id) {
                    connectionId = id; // we'll need this...
                    if (onReady) onReady();
                });
        });

    //connection.on("downloadResult", function (url) {
    //    writeLog('<a href="' + url + '">Download result file here</a>');
    //});

    //connection.on("countItResult", function (result) {
    //    fillCount(JSON.parse(result));
    //    writeLog(result);
    //});
    //connection.on("onComplete", function (message) {
    //    writeLog(message);
    //    let instance = $('#appBuckets').jstree(true);
    //    selectNode = instance.get_selected(true)[0];
    //    parentNode = instance.get_parent(selectNode);
    //    instance.refresh_node(parentNode);
    //});
    //connection.on("extractionFinished", function (data) {
    //    launchViewer(data.resourceUrn);
    //});

}
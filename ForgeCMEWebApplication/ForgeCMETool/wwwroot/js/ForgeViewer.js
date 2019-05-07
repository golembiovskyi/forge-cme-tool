var viewerApp;

$(document).ready(function () {
  var urn = getParameterByName('urn');
  if (urn !== null && urn !== '')
    launchViewer(urn);
});

function getParameterByName(name, url) {
  if (!url) url = window.location.href;
  name = name.replace(/[\[\]]/g, '\\$&');
  var regex = new RegExp('[?&]' + name + '(=([^&#]*)|&|#|$)'),
    results = regex.exec(url);
  if (!results) return null;
  if (!results[2]) return '';
  return decodeURIComponent(results[2].replace(/\+/g, ' '));
}

function launchViewer(urn, viewableId) {
  if (viewerApp != null) {
    var thisviewer = viewerApp.getCurrentViewer();
    if (thisviewer) {
      thisviewer.tearDown()
      thisviewer.finish()
      thisviewer = null
      $("#forgeViewer").empty();
    }
  }

  var options = {
    env: 'AutodeskProduction',
    getAccessToken: getForgeToken,
    api: 'derivativeV2' + (atob(urn.replace('_', '/')).indexOf('emea') > -1 ? '_EU' : '')
  };
  var documentId = 'urn:' + urn;
  Autodesk.Viewing.Initializer(options, function onInitialized() {
    viewerApp = new Autodesk.Viewing.ViewingApplication('forgeViewer');
    viewerApp.registerViewer(viewerApp.k3D, Autodesk.Viewing.Private.GuiViewer3D, { extensions: ['Autodesk.Sample.CivilExtension', 'Autodesk.Sample.MiniMapExtension'] });
    viewerApp.loadDocument(documentId, function (doc) {
      // We could still make use of Document.getSubItemsWithProperties()
      // However, when using a ViewingApplication, we have access to the **bubble** attribute,
      // which references the root node of a graph that wraps each object from the Manifest JSON.
      var viewables = viewerApp.bubble.search({ 'type': 'geometry' });
      if (viewables.length === 0) {
        console.error('Document contains no viewables.');
        return;
      }

      if (viewableId !== undefined) {
        viewables.forEach(function (viewable) {
          if (viewable.data.viewableID == viewableId)
            viewerApp.selectItem(viewable.data, onItemLoadSuccess, onItemLoadFail);
        });
      }
      else
        viewerApp.selectItem(viewables[0].data, onItemLoadSuccess, onItemLoadFail);
    }, onDocumentLoadFailure);
  });
}

function onDocumentLoadFailure(viewerErrorCode) {
  console.error('onDocumentLoadFailure() - errorCode:' + viewerErrorCode);
}

function onItemLoadSuccess(viewer, item) {
  // item loaded, any custom action?
  viewer.loadExtension('ObjectCodeExtension', { objectCodeProperty: 'BIMDATA - ObjectCode' });
}

function onItemLoadFail(errorCode) {
  console.error('onItemLoadFail() - errorCode:' + errorCode);
}

function getForgeToken(callback) {
  jQuery.ajax({
    url: '/api/forge/oauth/token',
    success: function (res) {
      callback(res.access_token, res.expires_in)
    }
  });
}
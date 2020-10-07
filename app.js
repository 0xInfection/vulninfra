const { mylocalServer, Config } = require('mylocal-website-builder')
const serveStatic = require('serve-static')
const path = require('path')
const isElectron = require('is-electron')
const eleventy = require('./11tyPublish')
const jekyll = require('./JekyllPublish')
const config = new Config()

// enable only local file system to store files
// and github to publish
config.ceOptions.enableSftp = false
config.ceOptions.enableFs = isElectron() || process.env.ENABLE_FS
config.ceOptions.githubClientId = process.env.GITHUB_CLIENT_ID || 'f124e4148bf9d633d58b'
config.ceOptions.githubClientSecret = process.env.GITHUB_CLIENT_SECRET || '1a8fcb93d5d0786e0ba16d81e8c118ce03eefece'

// allow to publish only in a local folder
config.publisherOptions.skipHostingSelection = false
config.publisherOptions.enableHostingUnifile = false
config.publisherOptions.enableHostingGhPages = false

// create the mylocal server
const mylocal = new mylocalServer(config)

// add custom services
mylocal.publishRouter.addHostingProvider(new eleventy(mylocal.unifile))
mylocal.publishRouter.addHostingProvider(new jekyll(mylocal.unifile))

// serve custom script
mylocal.app.use('/client.js', serveStatic(path.resolve('./client/client.js')))

// serve modified html
mylocal.app.use('/', serveStatic(path.resolve('./pub')))

// export mylocal so that the caller can start mylocal with mylocal.start(() => {})
module.exports = mylocal
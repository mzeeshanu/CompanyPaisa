// The parts of d3 the charts use. Importing the whole 'd3' package also brought in d3-selection and d3-transition, which
// patch each other when they load, so the bundler couldn't leave them out — code the site never ran.
export { extent, groups, least, max, min, range, rollups, sum } from 'd3-array';
export { scaleBand, scaleLinear, scalePoint, scaleSqrt } from 'd3-scale';
export { area, curveMonotoneX, line } from 'd3-shape';
